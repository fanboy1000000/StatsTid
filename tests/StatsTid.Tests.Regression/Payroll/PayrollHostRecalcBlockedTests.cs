using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S144 / TASK-14403 — HOST-LEVEL pins: the two payroll endpoints refuse a month with a mid-month
/// agreement-code change with a redacted 422, and write nothing.
///
/// <para>
/// <b>The story, in plain language.</b> An employee is employed all of March 2026. On 16 March
/// their agreement code changes (HK → AC). The live rule set cannot evaluate its whole-period rules
/// in two pieces, so the planner refuses the month. Before S144 the payroll host never saw the
/// change (it paid March under one agreement), and when the planner DID refuse a month (a profile
/// change) the refusal escaped as a bare 500. Now both endpoints answer 422 with
/// <c>kind = "payroll-recalc-blocked"</c>, the segment count and the cause names — and no date and
/// no employee id, because the planner's own message (which names both) never reaches the body.
/// </para>
///
/// <para>
/// <b>How "the catch is removed" reads in this harness.</b> The host runs in-process on TestServer,
/// in the Production environment, with no exception-handler middleware, so an exception that escapes
/// a handler is rethrown from the test's request call and the fact fails BEFORE its status
/// assertion. A Kestrel host would answer 500. The red conditions below write this case as
/// "escapes (500 on Kestrel)".
/// </para>
///
/// <para>
/// <b>Why at host level.</b> The service-level pins (TASK-14405's
/// <c>RecalcBlockedLiveRulesetTests</c>) build <see cref="PeriodCalculationService"/> by hand, WITH
/// the agreement-code repository, so they cannot see whether the HOST injects it. The repository
/// parameter is null-tolerant by ruling (R2): if the host ever stopped injecting it, the service
/// would silently see one segment and pay the month under one agreement. Only a call through the
/// real host's DI catches that — which is why fact (a) is not cuttable.
/// </para>
///
/// <para>
/// <b>Seed shape (load-bearing).</b> Employed across the WHOLE month (both employment dates NULL),
/// ONE <c>employee_profiles</c> row from 0001-01-01 (no profile change inside the month), March
/// entirely on the OK24 side of the 2026-04-01 OK transition, no local-profile activation — so the
/// agreement-code change on 16 March is the ONLY interior boundary and the EMPLOYED segment count
/// is exactly 2. A stray employment edge or profile change would make it 3 (or cut a segment off).
/// </para>
///
/// <para>
/// Docker-gated (Testcontainers Postgres + full <c>init.sql</c>): CI-verified, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollHostRecalcBlockedTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string OrgId = "STY_S144_HOST";
    private const string InitialAgreementCode = "HK";
    private const string SuccessorAgreementCode = "AC";
    private const string OkVersion = "OK24";
    private const string Kind = "payroll-recalc-blocked";
    private const string IsoDatePattern = @"\d{4}-\d{2}-\d{2}";

    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private const int Year = 2026;
    private const int Month = 3;

    private PostgreSqlContainer _container = null!;
    private DbConnectionFactory _db = null!;
    private PayrollHostFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        var cs = _container.GetConnectionString();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(cs);
        _db = new DbConnectionFactory(cs);
        await TestFixtures.SeedWageTypeMappingsAsync(_db);

        // The LIVE rule set — four AlignedWindow rules — is what refuses the split.
        _factory = new PayrollHostFactory(cs, new RuleRegistry().GetAll());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // (a) /recalculate
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (a) <c>POST /api/payroll/recalculate</c> as Global Admin, for an exported March with an
    /// agreement-code change recorded on 16 March → <b>422</b>, <c>kind = payroll-recalc-blocked</c>,
    /// <c>employedSegmentCount = 2</c>, <c>interiorBoundaryCauses = ["AgreementCodeChange"]</c>; the
    /// body contains no ISO date and not the employee id; <c>current_effective_lines</c> is
    /// byte-for-byte unchanged (the correction service plans before its transaction opens).
    ///
    /// <para>
    /// This fact also proves the host's DI wiring of
    /// <see cref="PeriodCalculationService"/>'s <c>userAgreementCodeRepo</c>: with a null repository
    /// the planner sees one segment, nothing refuses, and the endpoint answers 200.
    /// </para>
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) mutation M-14 — <c>BuildPlanForLegacyCallersAsync</c> skips the
    /// <c>AgreementCodeEffectiveDates</c> hydration (passes null): the month plans as ONE segment,
    /// the stubbed rule engine answers, the correction commits, and the endpoint returns <b>200</b> —
    /// <c>Assert.Equal(422, status)</c> trips (and the baseline changes). If instead the host stops
    /// registering <c>UserAgreementCodeRepository</c> in DI, the request FAILS — <b>200</b> or
    /// <b>500</b> (<c>EmploymentProfileResolver</c> needs the same repository, Program.cs:58, so
    /// resolution itself may throw) — and either way the 422 assertion goes red.
    /// (2) the <c>/recalculate</c> <c>PlannerInvariantViolation</c> catch is removed → escapes (500 on Kestrel),
    /// the fact fails before or at the 422 assertion.
    /// (3) mutation M-13 (<c>error = ex.Message</c>) → the body carries the period start
    /// (2026-03-01) and end (2026-03-31) — in ISO, and in machine-culture form where the message
    /// renders them so — and the employee id; <c>AssertRedactedProblem</c> asserts the body
    /// contains neither date in ISO, machine-culture or invariant-culture form, nor the employee id,
    /// so those assertions trip.
    /// </summary>
    [Fact]
    public async Task Recalculate_MidMonthAgreementCodeChange_Returns422_RedactedProblem_LinesUnchanged()
    {
        const string employeeId = "EMP-S144-HOST-RECALC";
        await SeedEmployeeAsync(employeeId);
        await PersistFirstExportRecordAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId);

        var body = new
        {
            profile = Profile(employeeId),
            entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart = Mar01,
            periodEnd = Mar31,
            previousFlexBalance = 0m,
            reason = "S144 host pin — agreement change recorded after export",
            idempotencyToken = Guid.NewGuid(),
        };

        var response = await PostAsGlobalAdminAsync("/api/payroll/recalculate", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertRedactedProblem(json, employeeId);

        Assert.Equal(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // (b) /calculate-and-export
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (b) <c>POST /api/payroll/calculate-and-export</c> as Global Admin, for an APPROVED March with
    /// an agreement-code change on 16 March → <b>422</b> with the same redacted problem; no
    /// <c>payroll_export_records</c> row is created and no <c>segment_manifests</c> row is written
    /// (the planner refuses before anything is calculated or persisted).
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) the export handler's <c>PlannerInvariantViolation</c> catch around
    /// <c>CalculateWithOutcomeAsync</c> is removed → the refusal escapes (500 on Kestrel);
    /// the fact fails before or at the 422 assertion.
    /// (2) mutation M-14 → one segment; the month calculates and the export record and manifest are
    /// committed; on this shared factory the post-commit delivery stub answers 404, so the handler
    /// returns 422 with an anonymous body (the calculation's fields plus <c>Success = false</c>,
    /// <c>ErrorMessage</c> and <c>ExportId</c>, Program.cs:414-426) that carries no <c>kind</c> —
    /// <c>AssertRedactedProblem</c> fails on the missing property (and both row counts are 1). Red,
    /// but not the 200 that (a) sees.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_MidMonthAgreementCodeChange_Returns422_NoExportRecord_NoManifest()
    {
        const string employeeId = "EMP-S144-HOST-EXPORT";
        await SeedEmployeeAsync(employeeId);
        await SeedApprovedPeriodAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var body = new
        {
            profile = Profile(employeeId),
            entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart = Mar01,
            periodEnd = Mar31,
            previousFlexBalance = 0m,
        };

        var response = await PostAsGlobalAdminAsync("/api/payroll/calculate-and-export", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertRedactedProblem(json, employeeId);

        Assert.Equal(0, await CountAsync("payroll_export_records", employeeId));
        Assert.Equal(0, await CountAsync("segment_manifests", employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // (c) raw /export + /export-period — S144 TASK-14410 (owner ruling 2026-09-29, QUAL-183)
    // ═════════════════════════════════════════════════════════════════════
    //
    // The two raw routes take lines the caller ALREADY calculated and never ran the planner, so a
    // Global Admin could export a month the calculating endpoints refuse. They now plan every
    // calendar month their lines fall in (through the same PCS builder) before anything is mapped.
    //
    // These facts run on DeliveringFactory(): the shared PayrollHostFactory stub answers the export
    // service's post-commit delivery POST (/api/payroll/receive) with 404, which turns a SUCCESSFUL,
    // committed export into a 422 ExportResult. Answering that one path with 200 makes "the route
    // exported" a clean 200, so a blinded guard is a status flip and not a look-alike 422.

    /// <summary>
    /// (c1) <c>POST /api/payroll/export</c> as Global Admin, with a caller-calculated March-2026
    /// <c>CalculationResult</c> (NORMAL_HOURS lines on 2 March and 20 March, either side of the
    /// agreement-code change on 16 March) and the employee's profile → <b>422</b>,
    /// <c>kind = payroll-recalc-blocked</c>, <c>employedSegmentCount = 2</c>, causes
    /// <c>["AgreementCodeChange"]</c>, redacted (no date, no employee id); NO
    /// <c>payroll_export_records</c> row for the employee's March.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) mutation M-14 — <c>BuildPlanForLegacyCallersAsync</c> passes null
    /// <c>AgreementCodeEffectiveDates</c>: the guard plans ONE segment and is blind, the route maps
    /// and exports, the delivery stub answers, and the endpoint returns <b>200</b> —
    /// the 422 assertion trips (and a March record exists).
    /// (2) the guard call is removed from the <c>/export</c> handler → the route exports → <b>200</b>,
    /// same assertion trips.
    /// (3) the guard is moved AFTER <c>ExportAsync</c> → the 422 may still come back, but the March
    /// record has already been committed — the zero-row assertion trips.
    /// </summary>
    [Fact]
    public async Task Export_MidMonthAgreementCodeChange_Returns422_RedactedProblem_NoExportRecord()
    {
        const string employeeId = "EMP-S144-HOST-RAWEXPORT";
        await SeedEmployeeAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var body = new PayrollExportRequest
        {
            CalculationResult = RawResult(employeeId, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 20)),
            Profile = Profile(employeeId),
        };

        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/export", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertRedactedProblem(json, employeeId);

        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, Month));
    }

    /// <summary>
    /// (c2) <c>POST /api/payroll/export-period</c> as Global Admin, with TWO caller-calculated
    /// results: February 2026 (no change inside it — it would export on its own, see (c3)) and
    /// March 2026 (the agreement-code change on 16 March) → <b>422</b> with the same redacted
    /// problem, and NO export record for EITHER month. All-or-nothing: the guard runs over every
    /// result's months before the first one is mapped, so the clean February is not written either.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) mutation M-14 (hydration nulled) → the guard is blind to March → both months export →
    /// <b>200</b>; the 422 assertion trips (and both records exist).
    /// (2) the guard call is removed from the <c>/export-period</c> handler → <b>200</b>, same trip.
    /// (3) the guard is moved AFTER <c>ExportAsync</c> → February and March records are already
    /// committed — the zero-row assertions trip.
    /// </summary>
    [Fact]
    public async Task ExportPeriod_MidMonthAgreementCodeChange_Returns422_RedactedProblem_NoExportRecord()
    {
        const string employeeId = "EMP-S144-HOST-RAWPERIOD";
        await SeedEmployeeAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var body = new PayrollPeriodExportRequest
        {
            CalculationResults = new List<CalculationResult>
            {
                RawResult(employeeId, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 16)),
                RawResult(employeeId, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 20)),
            },
            Profile = Profile(employeeId),
        };

        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/export-period", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertRedactedProblem(json, employeeId);

        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, 2));
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, Month));
    }

    /// <summary>
    /// (c3) No-regression: the SAME seed (agreement-code change on 16 March), but a February-2026
    /// result — no interior change in February — through <c>POST /api/payroll/export</c> →
    /// <b>200</b> and exactly one February <c>payroll_export_records</c> row. The guard refuses only
    /// the months the planner refuses; it does not turn every raw export into a 422.
    ///
    /// Green before AND after the guard (no mutation targets it); in neither frozen RED list — it is
    /// a GREEN spot check for both runs. It would go red if the guard over-refused (e.g. planned a
    /// fixed wider window reaching 16 March instead of the calendar month) or failed to resolve its
    /// dependencies in the host (the request fails).
    /// </summary>
    [Fact]
    public async Task Export_MonthWithoutInteriorChange_StillExports_200()
    {
        const string employeeId = "EMP-S144-HOST-RAWFEB";
        await SeedEmployeeAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var body = new PayrollExportRequest
        {
            CalculationResult = RawResult(employeeId, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 16)),
            Profile = Profile(employeeId),
        };

        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/export", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"expected 200, got {(int)response.StatusCode}: {json}");
        Assert.Equal(1, await CountExportRecordsAsync(employeeId, Year, 2));
    }

    // ═════════════════════════════════════════════════════════════════════
    // (d) Rule Engine outage — S144 TASK-14412 (Step 7a cycle 2, ruling B1)
    // ═════════════════════════════════════════════════════════════════════
    //
    // The planner refuses a split month only when the rule classification set it is given contains a
    // Reject / AlignedWindow rule. Before TASK-14412 the HTTP provider answered a Rule Engine outage
    // with an EMPTY set, so every route planned blind and the (c) months exported. Now the provider
    // throws RuleClassificationsUnavailableException and every planning route answers a fixed 503
    // before anything is written.
    //
    // These facts run on UnavailableRulesFactory(): the REAL HttpRuleClassificationProvider over a
    // client whose every call answers 503 (so the real provider throws inside the real host, and the
    // real handler maps it), plus the (c) delivering handler — so that a provider reverted to "empty"
    // lets the export complete as a clean 200 rather than a look-alike 422.

    /// <summary>
    /// (d1) <c>POST /api/payroll/export</c> with the (c1) seed and body, while the Rule Engine's
    /// classifications endpoint answers 503 → <b>503</b>, <c>kind = payroll-rules-unavailable</c>,
    /// <c>success = false</c>, no ISO date and no employee id in the body; NO March export record.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) the provider reverts to returning an empty set on failure → the guard is blind, the route
    /// maps and exports → <b>200</b> plus a March record; the 503 assertion trips.
    /// (2) the <c>RuleClassificationsUnavailableException</c> catch in
    /// <c>RefuseUnplannableMonthsAsync</c> is removed → escapes (500 on Kestrel); the fact fails before or at the 503 assertion.
    /// </summary>
    [Fact]
    public async Task Export_RulesUnavailable_Returns503_NoExportRecord()
    {
        const string employeeId = "EMP-S144-HOST-RULES-RAW";
        await SeedEmployeeAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var body = new PayrollExportRequest
        {
            CalculationResult = RawResult(employeeId, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 20)),
            Profile = Profile(employeeId),
        };

        var response = await PostAsGlobalAdminAsync(UnavailableRulesFactory(), "/api/payroll/export", body);
        var json = await response.Content.ReadAsStringAsync();

        AssertRulesUnavailable(response, json, employeeId);
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, Month));
    }

    /// <summary>
    /// (d2) <c>POST /api/payroll/export-period</c> with the (c2) seed and body (February + March),
    /// while the Rule Engine's classifications endpoint answers 503 → <b>503</b> with the fixed body;
    /// NO February and NO March export record.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) the provider reverts to returning an empty set on failure → both months export →
    /// <b>200</b> plus two records; the 503 assertion trips.
    /// (2) the catch in <c>RefuseUnplannableMonthsAsync</c> is removed → escapes (500 on Kestrel); the fact fails before or at the 503 assertion.
    /// </summary>
    [Fact]
    public async Task ExportPeriod_RulesUnavailable_Returns503_NoExportRecord()
    {
        const string employeeId = "EMP-S144-HOST-RULES-PERIOD";
        await SeedEmployeeAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var body = new PayrollPeriodExportRequest
        {
            CalculationResults = new List<CalculationResult>
            {
                RawResult(employeeId, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 16)),
                RawResult(employeeId, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 20)),
            },
            Profile = Profile(employeeId),
        };

        var response = await PostAsGlobalAdminAsync(UnavailableRulesFactory(), "/api/payroll/export-period", body);
        var json = await response.Content.ReadAsStringAsync();

        AssertRulesUnavailable(response, json, employeeId);
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, 2));
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, Month));
    }

    /// <summary>
    /// (d3) <c>POST /api/payroll/calculate-and-export</c> with the (b) seed and body (APPROVED
    /// March, agreement-code change on 16 March), while the Rule Engine's classifications endpoint
    /// answers 503 → <b>503</b> with the fixed body; zero <c>payroll_export_records</c> and zero
    /// <c>segment_manifests</c> rows (the plan is built before anything is calculated or persisted).
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) the provider reverts to returning an empty set on failure → the planner does not refuse,
    /// the month calculates and exports, delivery answers → <b>200</b> plus a record and a manifest;
    /// the 503 assertion trips.
    /// (2) the <c>RuleClassificationsUnavailableException</c> catch around
    /// <c>CalculateWithOutcomeAsync</c> is removed → escapes (500 on Kestrel); the fact fails before or at the 503 assertion.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_RulesUnavailable_Returns503_NoExportRecord_NoManifest()
    {
        const string employeeId = "EMP-S144-HOST-RULES-CALC";
        await SeedEmployeeAsync(employeeId);
        await SeedApprovedPeriodAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var body = new
        {
            profile = Profile(employeeId),
            entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart = Mar01,
            periodEnd = Mar31,
            previousFlexBalance = 0m,
        };

        var response = await PostAsGlobalAdminAsync(UnavailableRulesFactory(), "/api/payroll/calculate-and-export", body);
        var json = await response.Content.ReadAsStringAsync();

        AssertRulesUnavailable(response, json, employeeId);
        Assert.Equal(0, await CountAsync("payroll_export_records", employeeId));
        Assert.Equal(0, await CountAsync("segment_manifests", employeeId));
    }

    /// <summary>
    /// (d4) <c>POST /api/payroll/recalculate</c> with the (a) seed and body (exported March,
    /// agreement-code change recorded on 16 March), while the Rule Engine's classifications endpoint
    /// answers 503 → <b>503</b> with the fixed body; <c>current_effective_lines</c> is unchanged
    /// (the correction service plans before it opens its transaction).
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) the provider reverts to returning an empty set on failure → the planner does not refuse,
    /// the correction commits → <b>200</b> and the baseline changes; the 503 and baseline assertions
    /// trip.
    /// (2) the <c>RuleClassificationsUnavailableException</c> catch in the <c>/recalculate</c>
    /// handler is removed → escapes (500 on Kestrel); the fact fails before or at the 503 assertion.
    /// </summary>
    [Fact]
    public async Task Recalculate_RulesUnavailable_Returns503_LinesUnchanged()
    {
        const string employeeId = "EMP-S144-HOST-RULES-RECALC";
        await SeedEmployeeAsync(employeeId);
        await PersistFirstExportRecordAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId);

        var body = new
        {
            profile = Profile(employeeId),
            entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Mar01, Mar31),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart = Mar01,
            periodEnd = Mar31,
            previousFlexBalance = 0m,
            reason = "S144 host pin — Rule Engine outage during a correction",
            idempotencyToken = Guid.NewGuid(),
        };

        var response = await PostAsGlobalAdminAsync(UnavailableRulesFactory(), "/api/payroll/recalculate", body);
        var json = await response.Content.ReadAsStringAsync();

        AssertRulesUnavailable(response, json, employeeId);
        Assert.Equal(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId));
    }

    /// <summary>The shared host with TWO swaps on top: (1) the (c) delivering handler for every
    /// outbound client (POST <c>/api/payroll/receive</c> answers 200, everything else goes to the
    /// shared Rule Engine stub); (2) the REAL <see cref="HttpRuleClassificationProvider"/> in place
    /// of the in-memory set, over its own client whose every response is 503, with
    /// <see cref="JwtTokenService"/> and the logger taken from the host's own container. Derived
    /// factories are disposed with <see cref="_factory"/>.</summary>
    private WebApplicationFactory<RetroactiveCorrectionService> UnavailableRulesFactory() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new DeliveringClientFactory());

            services.RemoveAll<IRuleClassificationProvider>();
            services.AddSingleton<IRuleClassificationProvider>(sp => new HttpRuleClassificationProvider(
                new HttpClient(new TestFixtures.StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
                {
                    BaseAddress = new Uri("http://rule-engine.test"),
                },
                sp.GetRequiredService<JwtTokenService>(),
                sp.GetRequiredService<ILogger<HttpRuleClassificationProvider>>()));
        }));

    private static void AssertRulesUnavailable(HttpResponseMessage response, string json, string employeeId)
    {
        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"expected 503, got {(int)response.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("payroll-rules-unavailable", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        // The body is FIXED (Program.cs:618): exactly these three properties, so an added
        // upstream-detail field cannot slip past the pin (Step 7a cycle 3, C2).
        Assert.Equal(
            new[] { "error", "kind", "success" },
            root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        Assert.DoesNotMatch(IsoDatePattern, json);
        Assert.DoesNotContain(employeeId, json);
    }

    /// <summary>A caller-calculated result: one NORMAL_HOURS line (7.4 h) on each given date —
    /// the shape the raw routes receive (lines already computed; the route only maps them).</summary>
    private static CalculationResult RawResult(string employeeId, params DateOnly[] dates) => new()
    {
        RuleId = "NORM_CHECK_37H",
        EmployeeId = employeeId,
        Success = true,
        LineItems = dates
            .Select(d => new CalculationLineItem { TimeType = "NORMAL_HOURS", Hours = 7.4m, Rate = 0m, Date = d })
            .ToList(),
    };

    /// <summary>The shared host with ONE swap on top: the post-commit delivery POST to mock payroll
    /// (<c>/api/payroll/receive</c>) answers 200; every other outbound call still goes to the shared
    /// Rule Engine stub. Derived factories are disposed with <see cref="_factory"/>.</summary>
    private WebApplicationFactory<RetroactiveCorrectionService> DeliveringFactory() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new DeliveringClientFactory());
        }));

    private sealed class DeliveringClientFactory : IHttpClientFactory
    {
        // Exactly POST /api/payroll/receive — the path PayrollExportService posts delivery to
        // ({ServiceUrls:MockPayroll}/api/payroll/receive, via PostAsJsonAsync).
        private readonly HttpMessageHandler _handler = new TestFixtures.StubHandler(request =>
            request.Method == HttpMethod.Post
            && string.Equals(request.RequestUri?.AbsolutePath, "/api/payroll/receive", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : TestFixtures.DefaultRuleEngineHandler(request));

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private async Task<int> CountExportRecordsAsync(string employeeId, int year, int month)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<HttpResponseMessage> PostAsGlobalAdminAsync(
        WebApplicationFactory<RetroactiveCorrectionService> factory, string path, object body)
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, body.GetType()),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return await client.SendAsync(request);
    }

    // ─── Assertions ──────────────────────────────────────────────────────

    private static void AssertRedactedProblem(string json, string employeeId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(Kind, root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(2, root.GetProperty("employedSegmentCount").GetInt32());
        Assert.Equal(new[] { "AgreementCodeChange" },
            root.GetProperty("interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("ruleId").GetString()));

        Assert.DoesNotMatch(IsoDatePattern, json);
        // The refusal's OWN period dates, in every rendering the exception message could use
        // (ISO, the machine-culture form, the invariant-culture form) — a bare "no ISO date"
        // regex would pass a message that rendered them culture-formatted.
        foreach (var date in new[] { Mar01, Mar31 })
        {
            Assert.DoesNotContain(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), json);
            Assert.DoesNotContain(date.ToString(), json);
            Assert.DoesNotContain(date.ToString(CultureInfo.InvariantCulture), json);
        }
        Assert.DoesNotContain(employeeId, json);
    }

    // ─── HTTP ────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> PostAsGlobalAdminAsync(string path, object body)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return await client.SendAsync(request);
    }

    private static string MintGlobalAdminToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });

        // GLOBAL scope: OrgScopeValidator (the /calculate-and-export resource guard) admits a
        // GLOBAL-scoped actor for any employee.
        return tokenService.GenerateToken(
            employeeId: "ROOT-S144",
            name: "ROOT-S144",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static EmploymentProfile Profile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = InitialAgreementCode,
        OkVersion = OkVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    // ─── Seeding ─────────────────────────────────────────────────────────

    /// <summary>users + employee_profiles + user_agreement_codes (history-covering '0001-01-01'
    /// rows) + the organization. Employment dates both NULL: employed the whole month.</summary>
    private async Task SeedEmployeeAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, InitialAgreementCode, OkVersion, partTimeFraction: 1.0m);
    }

    /// <summary>Closes the live agreement-code row at <paramref name="effectiveFrom"/> (end-exclusive)
    /// and inserts the successor — written directly so the test controls the dates.</summary>
    private async Task SupersedeAgreementCodeAsync(string employeeId, string newAgreementCode, DateOnly effectiveFrom)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var close = new NpgsqlCommand(
            """
            UPDATE user_agreement_codes SET effective_to = @from
            WHERE user_id = @id AND effective_to IS NULL
            """, conn, tx))
        {
            close.Parameters.AddWithValue("id", employeeId);
            close.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @id, @code, @from, NULL, 2)
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("code", newAgreementCode);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        await tx.CommitAsync();
    }

    /// <summary>The FIRST-export record for March (<c>original_lines == current_effective_lines</c>),
    /// written with the production manifest serializer — one NORMAL_HOURS line, the shape the
    /// original export would have produced before the agreement change existed.</summary>
    private async Task PersistFirstExportRecordAsync(string employeeId)
    {
        var lines = new[]
        {
            new PayrollExportLine
            {
                EmployeeId = employeeId,
                WageType = "SLS_0110",
                Hours = 162.8m,
                Amount = 0m,
                PeriodStart = Mar01,
                PeriodEnd = Mar31,
                OkVersion = OkVersion,
                SourceRuleId = "NORM_CHECK_37H",
                SourceTimeType = "NORMAL_HOURS",
            },
        };
        var ordered = PayrollExportManifest.OrderLines(lines);
        var json = PayrollExportManifest.Serialize(ordered);
        var hash = PayrollExportManifest.ComputeContentHash(ordered);

        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records (
                export_id, period_id, employee_id, year, month, exported_at,
                original_lines, current_effective_lines, content_hash, source
            ) VALUES (
                @id, NULL, @emp, @year, @month, NOW(),
                @lines::jsonb, @lines::jsonb, @hash, 'CALCULATE_AND_EXPORT'
            )
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", Year);
        cmd.Parameters.AddWithValue("month", Month);
        cmd.Parameters.Add(new NpgsqlParameter("lines", NpgsqlDbType.Jsonb) { Value = json });
        cmd.Parameters.AddWithValue("hash", hash);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>The APPROVED monthly approval period the /calculate-and-export guard requires.</summary>
    private async Task SeedApprovedPeriodAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED',
                NOW(), @emp, 'ROOT-S144', NOW(), @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", Mar01);
        cmd.Parameters.AddWithValue("end", Mar31);
        cmd.Parameters.AddWithValue("agreement", InitialAgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ─── Reads ───────────────────────────────────────────────────────────

    private async Task<string> ReadCurrentEffectiveLinesAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT current_effective_lines::text FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", Year);
        cmd.Parameters.AddWithValue("m", Month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "no payroll_export_records row");
        return reader.GetString(0);
    }

    /// <summary>Row count for the employee in one of two fixed tables (the table name is a
    /// compile-time constant from the call sites, never input).</summary>
    private async Task<int> CountAsync(string table, string employeeId)
    {
        var sql = table switch
        {
            "payroll_export_records" => "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e",
            "segment_manifests" => "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "unsupported table"),
        };
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
