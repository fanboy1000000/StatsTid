using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
/// S145 / TASK-14503b (R49, stub-engine half) — HOST-LEVEL pins for what the Payroll host must
/// refuse, and what it must keep letting through, once payroll works week by week (QUAL-186):
/// the body-hours 400, the months-in-order 409, the order the refusals come in, the raw-route
/// carve-out, and Q7 (b) — a recalculation that names the next month.
///
/// <para>
/// <b>The story, in plain language.</b> A pay month is no longer judged on its own. Overtime
/// earned in the last days of one month is paid with the next month, because a week is settled
/// when it closes (ruling Q3). So three things change at the host:
/// <list type="number">
/// <item><b>Payroll reads the recorded hours itself</b> (R14). A request that still carries hours
/// is refused with a clear 400 (<c>payroll-entries-not-accepted</c>) instead of silently
/// ignoring them — silently dropped hours is exactly the defect class the sprint fixes.</item>
/// <item><b>Months must be approved in order</b> (A5, R5(b)). A month whose first Monday-to-Sunday
/// week reaches back into an EMPLOYED earlier month is calculated only once that earlier month is
/// APPROVED; until then the two calculating routes answer 409
/// (<c>payroll-prior-month-not-approved</c>) naming the earlier month's pay period. A month that
/// starts on a Monday, or whose employee was not yet hired in the earlier month, needs no earlier
/// approval. The raw <c>/export</c> routes bypass approval by design and stay untouched.</item>
/// <item><b>The refusals have an order</b> (N1): the planner's 422 first, then the week check's
/// 422, then this 409, then the rules. The 422s describe the month's own data, which approval
/// cannot fix, so they are reported before a sequencing state the operator can fix.</item>
/// </list>
/// And Q7 (b): recalculating an exported month whose closing week the next month already settled
/// tells the admin so — the <c>/recalculate</c> response and its audit row name the next month's
/// pay period — but only when the month does not end on a Sunday AND the next month has an export
/// record (R37).
/// </para>
///
/// <para>
/// <b>Why these facts speak only HTTP and SQL.</b> Every fact below compiles against today's code
/// (no new type is named), so each non-guard fact proves its RED by running before the
/// implementation exists, and its named production mutation (MD-id, plan § "RED evidence") is
/// observed in the Stage A evidence runs. A fact tagged <b>(guard)</b> passes today; it guards
/// against over-refusal (a 409 or a 422 where the month must still pay) and is exempt from the
/// "every non-guard Docker pin has a mutation" rule (R46a), though it still names the mutation
/// that would trip it where the catalogue has one.
/// </para>
///
/// <para>
/// <b>Fixture rules (plan § "Fixture conditions the table relies on").</b>
/// <list type="bullet">
/// <item>Bodies never carry hours (the two 400 facts are the exception: they send exactly one
/// entry on purpose). After TASK-14508 any body hours answer 400, and TASK-14508-T's paired pass
/// does not touch this file. Empty lists are accepted today and after.</item>
/// <item>Everything is the stub engine behind the real host — the statuses under test are decided
/// before any rule runs, or are 200 from the stub. The host answers its post-commit delivery POST
/// with 200 (the S144 (c) rationale), so a calculation that does export is a clean 200 and a
/// refused month is a status flip, not a look-alike 422.</item>
/// <item>H3 and H5 leave September with no approval row; H4 gives it EMPLOYEE_APPROVED (C-H2). H9
/// and H10 leave the earlier month unapproved by design (C-H1). The month's OWN approval is seeded
/// APPROVED wherever the route's 403 guard reads it. Months whose first week reaches an EMPLOYED
/// earlier month seed that month APPROVED in the Q7 facts (R26's host-fact rule), so what they
/// pin is Q7, not the 409.</item>
/// <item>Calendar premises (June 2026 starts on a Monday; 1 September is a Tuesday; 30 September
/// is not a Sunday; 31 May is) are asserted on entry to every fact, so a wrong premise reads as a
/// named failure.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>"Writes nothing"</b> means no payroll data: no <c>payroll_export_records</c> row (that row
/// IS the export lock, ADR-034), no <c>segment_manifests</c> row, and — for <c>/recalculate</c> —
/// an unchanged <c>current_effective_lines</c> baseline, no correction event, no correction audit
/// row and no idempotency marker. The refused request itself is still audited by the host's audit
/// middleware (<c>audit_log</c>), which is deliberate and not asserted either way (N-c4-4).
/// </para>
///
/// Docker-gated (Testcontainers Postgres + full <c>init.sql</c>): CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollHostWeeklyTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string OrgId = "STY_S145_HOST";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK26";
    private const string IsoDatePattern = @"\d{4}-\d{2}-\d{2}";

    private const string EntriesNotAcceptedKind = "payroll-entries-not-accepted";
    private const string PriorMonthNotApprovedKind = "payroll-prior-month-not-approved";
    private const string RecalcBlockedKind = "payroll-recalc-blocked";
    private const string MidWeekReason = "agreement-change-mid-week";

    // Every date below is checked against its weekday in AssertCalendarPremises().
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);
    private static readonly DateOnly May01 = new(2026, 5, 1);
    private static readonly DateOnly May31 = new(2026, 5, 31);
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun02 = new(2026, 6, 2);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);
    private static readonly DateOnly Aug01 = new(2026, 8, 1);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep03 = new(2026, 9, 3);
    private static readonly DateOnly Sep16 = new(2026, 9, 16);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private PostgreSqlContainer _container = null!;
    private DbConnectionFactory _db = null!;
    private PayrollHostFactory _factory = null!;
    private WebApplicationFactory<RetroactiveCorrectionService>? _host;

    public async Task InitializeAsync()
    {
        AssertCalendarPremises();

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

        // The LIVE rule set — four AlignedWindow rules — is what refuses a split month (H9).
        _factory = new PayrollHostFactory(cs, new RuleRegistry().GetAll());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>The shared host with ONE swap on top: the post-commit delivery POST to mock payroll
    /// (<c>/api/payroll/receive</c>) answers 200; every other outbound call still goes to the shared
    /// Rule Engine stub. Built once per fact; derived factories are disposed with
    /// <see cref="_factory"/>.</summary>
    private WebApplicationFactory<RetroactiveCorrectionService> Host =>
        _host ??= _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new DeliveringClientFactory());
        }));

    // ═════════════════════════════════════════════════════════════════════
    // Body hours are refused — R14 (TASK-14508)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// H1. <c>POST /api/payroll/calculate-and-export</c> for an APPROVED June 2026 whose BODY carries
    /// one time entry → <b>400</b>, <c>kind = payroll-entries-not-accepted</c>, and nothing is
    /// calculated, exported or recorded. June starts on a Monday, so no earlier approval is in play and the
    /// fact holds wherever the 400 sits relative to the approval guard (N1): the month is APPROVED.
    ///
    /// <para>
    /// <i>Plain language:</i> payroll now reads the employee's recorded hours itself. Nobody sends
    /// hours in practice, and a caller who did could leave out a day without anyone noticing, so a
    /// body that carries hours is refused loudly rather than silently ignored.
    /// </para>
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) before TASK-14508 the host accepts the hours: the month calculates, exports and the
    /// delivery stub answers <b>200</b> — <c>Assert.True(status == 400)</c> trips (and an export
    /// record exists). (2) mutation MD-19 (plan § "RED evidence") — the non-empty-list 400 is removed
    /// from the calculating handlers → the same 200, the same assertion.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_BodyCarriesEntry_Returns400_WritesNothing()
    {
        const string employeeId = "EMP-S145-H1";
        await SeedEmployeeAsync(employeeId);
        await SeedApprovalAsync(employeeId, Jun01, Jun30, "APPROVED");

        var body = CalculateBody(employeeId, Jun01, Jun30,
            entries: TestFixtures.WeekdayEntriesForPeriod(employeeId, Jun02, Jun02));

        var (status, json) = await PostAsync(Host, "/api/payroll/calculate-and-export", body);

        Assert.True(status == HttpStatusCode.BadRequest, $"expected 400, got {(int)status}: {json}");
        AssertEntriesNotAccepted(json, employeeId);

        Assert.Equal(0, await CountExportRecordsAsync(employeeId));
        Assert.Equal(0, await CountManifestsAsync(employeeId));
    }

    /// <summary>
    /// H2. <c>POST /api/payroll/recalculate</c> for an APPROVED and EXPORTED June 2026 whose BODY
    /// carries one time entry → <b>400</b> <c>payroll-entries-not-accepted</c>; the export baseline
    /// (<c>current_effective_lines</c>) is unchanged and no correction event, correction audit row,
    /// manifest or idempotency marker is written.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) before TASK-14508 the correction accepts the hours and commits → <b>200</b> and the
    /// baseline changes; the 400 assertion trips. (2) mutation MD-19 — the non-empty-list 400 is
    /// removed from the handlers → the same 200, the same assertion.
    /// </summary>
    [Fact]
    public async Task Recalculate_BodyCarriesEntry_Returns400_WritesNothing()
    {
        const string employeeId = "EMP-S145-H2";
        var token = Guid.NewGuid();
        await SeedEmployeeAsync(employeeId);
        await SeedApprovalAsync(employeeId, Jun01, Jun30, "APPROVED");
        await PersistExportRecordAsync(employeeId, 2026, 6);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId, 2026, 6);

        var body = RecalculateBody(employeeId, Jun01, Jun30, token,
            entries: TestFixtures.WeekdayEntriesForPeriod(employeeId, Jun02, Jun02));

        var (status, json) = await PostAsync(Host, "/api/payroll/recalculate", body);

        Assert.True(status == HttpStatusCode.BadRequest, $"expected 400, got {(int)status}: {json}");
        AssertEntriesNotAccepted(json, employeeId);

        await AssertRecalculationWroteNothingAsync(employeeId, token, linesBefore, 2026, 6);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Months in order — the 409 (A5, R5(b))
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// H3. <c>POST /api/payroll/calculate-and-export</c> for an APPROVED October 2026 while September
    /// has NO approval row → <b>409</b>, <c>kind = payroll-prior-month-not-approved</c>,
    /// <c>success = false</c>, a non-empty <c>error</c>, and September's pay period
    /// (<c>periodStart</c> 2026-09-01, <c>periodEnd</c> 2026-09-30) — the only dates in the body, and
    /// no employee id. Nothing is written: no export record, no manifest.
    ///
    /// <para>
    /// <i>Plain language:</i> October's first week (28 September – 4 October) holds three
    /// September days, and payroll pays that week's overtime with October. It will not do so before
    /// September is APPROVED, because September's hours could still change under it. The refusal is
    /// fixable by the operator (approve September), so it names September.
    /// </para>
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) before TASK-14512 there is no such check: the October calculation answers <b>200</b> —
    /// <c>Assert.True(status == 409)</c> trips. (2) mutation MD-21 — the
    /// <c>IPriorPeriodApprovalReader</c> registration is removed from the host, so PCS gets a null reader
    /// and its never-silent throw (R21) escapes (500 on Kestrel; the request call throws on this
    /// in-process host) — the fact fails before its 409 assertion.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_October_SeptemberNotApproved_Returns409_NamesSeptember_WritesNothing()
    {
        const string employeeId = "EMP-S145-H3";
        await SeedEmployeeAsync(employeeId);
        await SeedApprovalAsync(employeeId, Oct01, Oct31, "APPROVED");
        // September: deliberately NO approval row (C-H2).

        var (status, json) = await PostAsync(Host, "/api/payroll/calculate-and-export",
            CalculateBody(employeeId, Oct01, Oct31));

        Assert.True(status == HttpStatusCode.Conflict, $"expected 409, got {(int)status}: {json}");
        AssertPriorMonthNotApproved(json, employeeId, Sep01, Sep30);

        Assert.Equal(0, await CountExportRecordsAsync(employeeId));
        Assert.Equal(0, await CountManifestsAsync(employeeId));
    }

    /// <summary>
    /// H4. As H3, but September is EMPLOYEE_APPROVED (the employee signed; the manager has not) →
    /// the same <b>409</b>, naming September. EMPLOYEE_APPROVED is not enough: the Skema save lock
    /// already holds for it (<c>ApprovalPeriodSaveLock.cs:30</c>), yet the hours are not yet approved.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) before TASK-14512 → <b>200</b>; the 409 assertion trips. (2) mutation MD-22 — the check's
    /// predicate also accepts EMPLOYEE_APPROVED → the October calculation proceeds, <b>200</b>; the 409
    /// assertion trips (H3 and H5 stay 409: their September has no row). (3) mutation MD-21 — the
    /// reader registration removed → the never-silent throw escapes.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_October_SeptemberEmployeeApproved_Returns409()
    {
        const string employeeId = "EMP-S145-H4";
        await SeedEmployeeAsync(employeeId);
        await SeedApprovalAsync(employeeId, Oct01, Oct31, "APPROVED");
        await SeedApprovalAsync(employeeId, Sep01, Sep30, "EMPLOYEE_APPROVED");

        var (status, json) = await PostAsync(Host, "/api/payroll/calculate-and-export",
            CalculateBody(employeeId, Oct01, Oct31));

        Assert.True(status == HttpStatusCode.Conflict, $"expected 409, got {(int)status}: {json}");
        AssertPriorMonthNotApproved(json, employeeId, Sep01, Sep30);

        Assert.Equal(0, await CountExportRecordsAsync(employeeId));
        Assert.Equal(0, await CountManifestsAsync(employeeId));
    }

    /// <summary>
    /// H5. <c>POST /api/payroll/recalculate</c> for October 2026 — exported FIRST through the raw
    /// <c>/api/payroll/export</c> route (which bypasses approval, so October has no approval row and
    /// the route's own precondition, "the month is exported", is met) — while September has no
    /// approval row → <b>409</b> naming September. The recalculation writes nothing: the October
    /// baseline is unchanged, and there is no correction event, audit row, manifest or marker.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) before TASK-14512 → the correction commits, <b>200</b>; the 409 assertion trips (and the
    /// baseline changes). (2) mutation MD-21 — the reader registration removed → the never-silent
    /// throw escapes before the 409 assertion. (A prior-month check moved ahead of the per-plan lane,
    /// MD-24, leaves this fact at 409 either way.)
    /// </summary>
    [Fact]
    public async Task Recalculate_October_SeptemberNotApproved_Returns409()
    {
        const string employeeId = "EMP-S145-H5";
        var token = Guid.NewGuid();
        await SeedEmployeeAsync(employeeId);
        // September: deliberately NO approval row (C-H2).
        await ExportRawAsync(employeeId, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20));
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId, 2026, 10);

        var (status, json) = await PostAsync(Host, "/api/payroll/recalculate",
            RecalculateBody(employeeId, Oct01, Oct31, token));

        Assert.True(status == HttpStatusCode.Conflict, $"expected 409, got {(int)status}: {json}");
        AssertPriorMonthNotApproved(json, employeeId, Sep01, Sep30);

        await AssertRecalculationWroteNothingAsync(employeeId, token, linesBefore, 2026, 10);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Who needs no earlier approval — guards (A5)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// H6 <b>(guard)</b>. <c>POST /api/payroll/calculate-and-export</c> for an APPROVED June 2026 with
    /// NO May approval row → <b>200</b>. June 1 is a Monday, so the month's first week holds no May
    /// day: there is nothing earlier to wait for.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): passes today (no check
    /// exists), and carries no MD-id. It trips the day the check is written too broadly — keyed on
    /// "the month has a previous month" instead of "the first week holds EMPLOYED days of the
    /// previous month" — which answers <b>409</b> here and trips <c>Assert.True(status == 200)</c>.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_June2026_StartsMonday_NoPriorApprovalNeeded_200()
    {
        const string employeeId = "EMP-S145-H6";
        await SeedEmployeeAsync(employeeId);
        await SeedApprovalAsync(employeeId, Jun01, Jun30, "APPROVED");
        // May: deliberately NO approval row.

        var (status, json) = await PostAsync(Host, "/api/payroll/calculate-and-export",
            CalculateBody(employeeId, Jun01, Jun30));

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        AssertCalculationSucceeded(json);
    }

    /// <summary>
    /// H7 <b>(guard)</b>. A starter whose first day is 1 September (a Tuesday): the month's first
    /// week starts on 31 August, but the employee was not EMPLOYED then, so there are no earlier
    /// EMPLOYED days to wait for. September is APPROVED, August has no approval row → <b>200</b>.
    /// The employee's profile and agreement-code rows keep the history-covering default anchor (the
    /// hire-dated first row is H8's subject, not this fact's).
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): passes today, carries no
    /// MD-id. It trips when the check counts days the employee was not employed — answering
    /// <b>409</b> for a hire on the 1st — and trips <c>Assert.True(status == 200)</c>.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_StarterOnFirst_NoPriorApprovalNeeded_200()
    {
        const string employeeId = "EMP-S145-H7";
        await SeedEmployeeAsync(employeeId);
        await SetEmploymentStartAsync(employeeId, Sep01);
        await SeedApprovalAsync(employeeId, Sep01, Sep30, "APPROVED");
        // August: deliberately NO approval row.

        var (status, json) = await PostAsync(Host, "/api/payroll/calculate-and-export",
            CalculateBody(employeeId, Sep01, Sep30));

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        AssertCalculationSucceeded(json);
    }

    /// <summary>
    /// H8 <b>(guard)</b>. A starter hired Thursday 3 September: 31 August is not EMPLOYED, so no
    /// August approval is needed → <b>200</b>. Under R43 the employee's agreement-code row is a
    /// <b>version-1 row dated 3 September — the hire date</b>, not the <c>0001-01-01</c> default
    /// (the profile row too). So this fact also pins that a hire-dated first row is NOT an agreement
    /// change: on a non-Monday inside a settled week it would otherwise read as a mid-week change
    /// and refuse the month.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): passes today (the planner
    /// absorbs a hire date: one EMPLOYED segment, <c>PeriodPlanner.cs:355-356</c>). Mutation MD-46
    /// (the hire-date filter, TASK-14512 Do 1, dropped) → the row dated 3 September counts as a
    /// non-Monday agreement change inside the week of 31 August – 6 September → <b>422</b>
    /// <c>agreement-change-mid-week</c>; <c>Assert.True(status == 200)</c> trips.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_StarterThursday3Sep_NoAugustApprovalNeeded_200()
    {
        const string employeeId = "EMP-S145-H8";
        await SeedEmployeeAsync(employeeId, effectiveFrom: Sep03);
        await SetEmploymentStartAsync(employeeId, Sep03);
        await SeedApprovalAsync(employeeId, Sep01, Sep30, "APPROVED");
        // August: deliberately NO approval row.

        var (status, json) = await PostAsync(Host, "/api/payroll/calculate-and-export",
            CalculateBody(employeeId, Sep01, Sep30));

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        AssertCalculationSucceeded(json);
    }

    // ═════════════════════════════════════════════════════════════════════
    // The refusals have an order — N1: planner 422 → week-check 422 → prior-month 409 → rules
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// H9. A SPLIT month with an unapproved earlier month: September 2026 with an agreement-code
    /// change on Wednesday 16 September (an interior boundary the live rule set cannot evaluate in
    /// two pieces) and August with no approval row → the planner's <b>422</b> comes first: the S144
    /// body, <c>ruleId</c> present, two EMPLOYED segments, cause <c>AgreementCodeChange</c>, no date
    /// and no employee id — NOT the 409.
    ///
    /// <para>
    /// <i>Plain language:</i> the planner refuses a month whose own data cannot be calculated at all.
    /// Approving August would not change that, so telling the operator to approve August first would
    /// send them on a detour. The month's own problem is reported first.
    /// </para>
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): this fact passes today (no
    /// prior-month check exists), so it carries no pre-implementation RED. Mutation MD-24 (the
    /// prior-month check runs before the per-plan lane's call to <c>BuildPlanForLegacyCallersAsync</c>,
    /// i.e. before the planner) → <b>409</b> instead of 422; <c>Assert.True(status == 422)</c> trips.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_SplitMonth_PriorNotApproved_AnswersPlanner422First()
    {
        const string employeeId = "EMP-S145-H9";
        await SeedEmployeeAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, "AC", Sep16);
        await SeedApprovalAsync(employeeId, Sep01, Sep30, "APPROVED");
        // August: deliberately NO approval row (C-H1: H9 leaves the earlier month unapproved).

        var (status, json) = await PostAsync(Host, "/api/payroll/calculate-and-export",
            CalculateBody(employeeId, Sep01, Sep30));

        Assert.True(status == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)status}: {json}");
        AssertPlannerRefusal(json, employeeId, Sep01, Sep30, Sep16);

        Assert.Equal(0, await CountExportRecordsAsync(employeeId));
        Assert.Equal(0, await CountManifestsAsync(employeeId));
    }

    /// <summary>
    /// H10 (R31a). A month with a MID-WEEK agreement change and an unapproved earlier month: HK → AC
    /// effective Tuesday 1 September 2026 (inside the week 31 August – 6 September, which September
    /// settles), September APPROVED, August with no approval row → the week check's <b>422</b>
    /// (<c>kind = payroll-recalc-blocked</c>, <c>reason = agreement-change-mid-week</c>, cause
    /// <c>AgreementCodeChange</c>, no <c>ruleId</c>, no date, no employee id) comes first — NOT the
    /// 409. Nothing is written.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) before TASK-14512 there is no week check and no 409: the month calculates, <b>200</b>;
    /// <c>Assert.True(status == 422)</c> trips. Mutations (plan § "RED evidence"): MD-23 (the
    /// prior-month check runs before the week check) → <b>409</b>; MD-24 (it runs before the planner
    /// step) → <b>409</b>; MD-26 (the week-check call removed from the per-plan lane) → <b>409</b>;
    /// MD-27 (the agreement read keeps <c>afterExclusive: periodStart</c>, blind to 1 September) →
    /// <b>409</b> (in run EA-2, where MD-21 also nulls the reader, the never-silent throw escapes
    /// instead) — each trips the 422 assertion.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_AgreementChangeTue1Sep_AugustNotApproved_AnswersWeekCheck422First()
    {
        const string employeeId = "EMP-S145-H10";
        await SeedEmployeeAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, "AC", Sep01);
        await SeedApprovalAsync(employeeId, Sep01, Sep30, "APPROVED");
        // August: deliberately NO approval row (C-H1: H10 leaves the earlier month unapproved).

        var (status, json) = await PostAsync(Host, "/api/payroll/calculate-and-export",
            CalculateBody(employeeId, Sep01, Sep30));

        Assert.True(status == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)status}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep01, Sep30);

        Assert.Equal(0, await CountExportRecordsAsync(employeeId));
        Assert.Equal(0, await CountManifestsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // The raw route is unaffected — guard (A5: "the raw routes bypass approval by design")
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// H11 <b>(guard)</b>. The raw <c>POST /api/payroll/export</c> for October 2026 with September
    /// not approved → <b>200</b>, and exactly one October export record. The raw routes take lines
    /// the caller already calculated and bypass approval by design (admin retroactive and internal
    /// calls); the months-in-order check belongs to the two calculating routes only.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): passes today, carries no
    /// MD-id. It trips the day the raw-route guard (<c>RefuseUnplannableMonthsAsync</c>) also consults
    /// the earlier month's approval → <b>409</b>; <c>Assert.True(status == 200)</c> trips.
    /// </summary>
    [Fact]
    public async Task Export_October_SeptemberNotApproved_RawRouteUnaffected_200()
    {
        const string employeeId = "EMP-S145-H11";
        await SeedEmployeeAsync(employeeId);
        // No approval row for any month: the raw route does not read approval.

        var (status, json) = await PostAsync(Host, "/api/payroll/export",
            RawExportBody(employeeId, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 20)));

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        Assert.Equal(1, await CountExportRecordsAsync(employeeId, 2026, 10));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Q7 (b) — a recalculation names the next month (A6, ruled by the owner 2026-10-01; R37)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// H12. <c>POST /api/payroll/recalculate</c> for exported September 2026 while October is also
    /// exported → 200, and BOTH the response and the correction's audit row name October's pay period
    /// (2026-10-01 … 2026-10-31). September's own period stays what it was (response
    /// <c>periodStart</c>/<c>periodEnd</c>, audit <c>originalPeriodStart</c>). Nothing is written to
    /// October: its export baseline is unchanged.
    ///
    /// <para>
    /// <i>Plain language:</i> September's last week (28 September – 4 October) was settled — and its
    /// overtime paid — with October. Recalculating September corrects September alone (ADR-034 D5), so
    /// October's figure for that shared week can go stale. The system cannot fix October itself, so it
    /// tells the admin: "recalculate October too". The member names that carry the October period are
    /// the implementation's to choose (the brief names none; the event's are
    /// <c>NextPeriodToRecalculateStart/End</c>), so this fact asserts the VALUES: October's first and
    /// last day appear in the response and in the audit details, and nowhere in today's body.
    /// </para>
    ///
    /// Setup: August is APPROVED (September's first week reaches 31 August, R26's host-fact rule), so
    /// the months-in-order check does not interfere; September and October carry export records (the
    /// route's precondition, and Q7's "next month exported" condition).
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) before TASK-14512 the response and the audit row carry only September's dates; the
    /// assertion that October's first day is among the response's date values trips. (2) mutation
    /// MD-28 — the Q7 (b) block removed from <c>RetroactiveCorrectionService</c> → the same.
    /// (3) mutation MD-21 — the reader registration removed → September's opening week reaches August,
    /// the never-silent throw escapes before the response.
    /// </summary>
    [Fact]
    public async Task Recalculate_ExportedSeptember_OctoberExported_ResponseAndAuditNameOctober()
    {
        const string employeeId = "EMP-S145-H12";
        var token = Guid.NewGuid();
        await SeedEmployeeAsync(employeeId);
        await SeedApprovalAsync(employeeId, Aug01, Aug31, "APPROVED");
        await PersistExportRecordAsync(employeeId, 2026, 9);
        await PersistExportRecordAsync(employeeId, 2026, 10);
        var octoberLinesBefore = await ReadCurrentEffectiveLinesAsync(employeeId, 2026, 10);

        var (status, json) = await PostAsync(Host, "/api/payroll/recalculate",
            RecalculateBody(employeeId, Sep01, Sep30, token));

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        using var response = JsonDocument.Parse(json);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean(), json);
        Assert.Equal("2026-09-01", response.RootElement.GetProperty("periodStart").GetString());
        Assert.Equal("2026-09-30", response.RootElement.GetProperty("periodEnd").GetString());

        var responseDates = StringLeaves(response.RootElement);
        // October's first and last day, wherever the implementation carries them.
        Assert.Contains("2026-10-01", responseDates);
        Assert.Contains("2026-10-31", responseDates);

        var auditRow = Assert.Single(await ReadCorrectionAuditDetailsAsync(employeeId));
        using var audit = JsonDocument.Parse(auditRow);
        Assert.Equal("2026-09-01", audit.RootElement.GetProperty("originalPeriodStart").GetString());
        var auditDates = StringLeaves(audit.RootElement);
        Assert.Contains("2026-10-01", auditDates);
        Assert.Contains("2026-10-31", auditDates);

        // Q7 (b) only POINTS at October; it writes nothing there.
        Assert.Equal(octoberLinesBefore, await ReadCurrentEffectiveLinesAsync(employeeId, 2026, 10));
    }

    /// <summary>
    /// H13 <b>(guard)</b>. As H12, but October is NOT exported → the recalculation succeeds (200) and
    /// names nothing: no October date in the response, none in the audit row. R37's rule names the
    /// next month only if it has an export record — there is nothing yet to go stale.
    /// The fact first proves the recalculation really ran (200, <c>success</c>, one audit row), so
    /// "names nothing" cannot pass by the route having refused.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): passes today (nothing is ever
    /// named). Mutation MD-50 (Q7 (b) names the next month without its two conditions) → October is
    /// named although it is not exported; the assertion that October's first day is absent trips.
    /// Mutation MD-21 (reader registration removed) → the never-silent throw escapes.
    /// </summary>
    [Fact]
    public async Task Recalculate_ExportedSeptember_OctoberNotExported_NamesNothing()
    {
        const string employeeId = "EMP-S145-H13";
        var token = Guid.NewGuid();
        await SeedEmployeeAsync(employeeId);
        await SeedApprovalAsync(employeeId, Aug01, Aug31, "APPROVED");
        await PersistExportRecordAsync(employeeId, 2026, 9);
        // October: deliberately NOT exported.

        var (status, json) = await PostAsync(Host, "/api/payroll/recalculate",
            RecalculateBody(employeeId, Sep01, Sep30, token));

        await AssertRecalculatedAndNamedNothingAsync(
            employeeId, status, json, nextPeriodStart: "2026-10-01", nextPeriodEnd: "2026-10-31");
    }

    /// <summary>
    /// H14 <b>(guard)</b>. May 2026 ends on a Sunday (31 May), so its closing week closes inside May
    /// and June settles none of it. Recalculating exported May — with June ALSO exported, so that only
    /// the Sunday condition keeps June out — names nothing: no June date in the response or the audit
    /// row. April is APPROVED (May's first week, 27 April – 3 May, reaches April, R26's host-fact
    /// rule), so the months-in-order check does not interfere. Like H13, the fact first proves the
    /// recalculation really ran.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): passes today. Mutation MD-50
    /// (Q7 (b) names the next month without its two conditions) → June is named although May's last
    /// day is a Sunday; the assertion that June's first day is absent trips. Mutation MD-21 (reader
    /// registration removed) → the never-silent throw escapes.
    /// </summary>
    [Fact]
    public async Task Recalculate_ExportedMay2026_MonthEndsSunday_NamesNothing()
    {
        const string employeeId = "EMP-S145-H14";
        var token = Guid.NewGuid();
        await SeedEmployeeAsync(employeeId);
        await SeedApprovalAsync(employeeId, Apr01, Apr30, "APPROVED");
        await PersistExportRecordAsync(employeeId, 2026, 5);
        await PersistExportRecordAsync(employeeId, 2026, 6);

        var (status, json) = await PostAsync(Host, "/api/payroll/recalculate",
            RecalculateBody(employeeId, May01, May31, token));

        await AssertRecalculatedAndNamedNothingAsync(
            employeeId, status, json, nextPeriodStart: "2026-06-01", nextPeriodEnd: "2026-06-30");
    }

    // ─── Assertions ──────────────────────────────────────────────────────

    /// <summary>The week-3 premises every fact leans on, asserted so a wrong calendar reads as a
    /// named failure rather than as a mysterious status.</summary>
    private static void AssertCalendarPremises()
    {
        Assert.Equal(DayOfWeek.Monday, Jun01.DayOfWeek);     // June 2026 starts on a Monday (H6)
        Assert.Equal(DayOfWeek.Tuesday, Sep01.DayOfWeek);    // 1 September is mid-week (H7, H10)
        Assert.Equal(DayOfWeek.Thursday, Sep03.DayOfWeek);   // the starter's hire date (H8)
        Assert.Equal(DayOfWeek.Wednesday, Sep16.DayOfWeek);  // an interior, mid-week change (H9)
        Assert.Equal(DayOfWeek.Wednesday, Sep30.DayOfWeek);  // September does not end on a Sunday (H12, H13)
        Assert.Equal(DayOfWeek.Thursday, Oct01.DayOfWeek);   // October's first week reaches September (H3-H5)
        Assert.Equal(DayOfWeek.Friday, May01.DayOfWeek);     // May's first week reaches April (H14)
        Assert.Equal(DayOfWeek.Sunday, May31.DayOfWeek);     // May ends on a Sunday (H14)
    }

    private static void AssertEntriesNotAccepted(string json, string employeeId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(EntriesNotAcceptedKind, root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));

        // A fixed sentence: it echoes neither the refused entry's date nor the employee.
        Assert.DoesNotMatch(IsoDatePattern, json);
        Assert.DoesNotContain(employeeId, json);
    }

    private static void AssertPriorMonthNotApproved(
        string json, string employeeId, DateOnly priorStart, DateOnly priorEnd)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(PriorMonthNotApprovedKind, root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));

        // The earlier month's PAY period — and nothing else date-shaped (no employment date, no
        // echo of the month being calculated).
        Assert.Equal(priorStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            root.GetProperty("periodStart").GetString());
        Assert.Equal(priorEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            root.GetProperty("periodEnd").GetString());
        var datesInBody = Regex.Matches(json, IsoDatePattern).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { priorStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), priorEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            datesInBody);
        Assert.DoesNotContain(employeeId, json);
    }

    /// <summary>The planner's S144 split refusal, as H9 expects it: <c>ruleId</c> present, two
    /// EMPLOYED segments, no date (period start/end or the change date, in any rendering) and no
    /// employee id — and NOT the week check's variant.</summary>
    private static void AssertPlannerRefusal(
        string json, string employeeId, DateOnly periodStart, DateOnly periodEnd, DateOnly changeDate)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(RecalcBlockedKind, root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(2, root.GetProperty("employedSegmentCount").GetInt32());
        Assert.Equal(new[] { "AgreementCodeChange" },
            root.GetProperty("interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("ruleId").GetString()),
            "the planner's refusal names the whole-period rule that refused the split");
        Assert.False(
            root.TryGetProperty("reason", out var reason) && reason.GetString() == MidWeekReason,
            "the week check's variant must not answer a month the planner refuses");

        AssertNoDatesOrEmployee(json, employeeId, periodStart, periodEnd, changeDate);
    }

    /// <summary>The week check's 422 variant (A5, Q5 (c)): <c>kind</c> payroll-recalc-blocked,
    /// <c>reason</c> agreement-change-mid-week, cause <c>AgreementCodeChange</c>, no <c>ruleId</c>,
    /// no date and no employee id. (Copied, not shared, with <c>PayrollHostRecalcBlockedTests</c>:
    /// the two files may not share a helper.)</summary>
    private static void AssertWeekCheckRefusal(string json, string employeeId, params DateOnly[] datesThatMustNotLeak)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(RecalcBlockedKind, root.GetProperty("kind").GetString());
        Assert.Equal(MidWeekReason, root.GetProperty("reason").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
        Assert.Equal(new[] { "AgreementCodeChange" },
            root.GetProperty("interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.True(
            !root.TryGetProperty("ruleId", out var ruleId) || ruleId.ValueKind == JsonValueKind.Null,
            "the week check's variant carries no ruleId (it is not a rule's refusal)");

        AssertNoDatesOrEmployee(json, employeeId, datesThatMustNotLeak);
    }

    private static void AssertNoDatesOrEmployee(string json, string employeeId, params DateOnly[] dates)
    {
        Assert.DoesNotMatch(IsoDatePattern, json);
        // The dates in every rendering a message could use (ISO, machine culture, invariant
        // culture) — a bare "no ISO date" regex would pass a message that rendered them
        // culture-formatted.
        foreach (var date in dates)
        {
            Assert.DoesNotContain(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), json);
            Assert.DoesNotContain(date.ToString(), json);
            Assert.DoesNotContain(date.ToString(CultureInfo.InvariantCulture), json);
        }
        Assert.DoesNotContain(employeeId, json);
    }

    private static void AssertCalculationSucceeded(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean(), json);
    }

    /// <summary>"Writes nothing" for a refused <c>/recalculate</c>: the export baseline is
    /// unchanged and there is no correction event, no correction audit row, no manifest and no
    /// idempotency marker.</summary>
    private async Task AssertRecalculationWroteNothingAsync(
        string employeeId, Guid token, string linesBefore, int year, int month)
    {
        Assert.Equal(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId, year, month));
        Assert.Equal(0, await CountCorrectionEventsAsync(employeeId));
        Assert.Empty(await ReadCorrectionAuditDetailsAsync(employeeId));
        Assert.Equal(0, await CountManifestsAsync(employeeId));
        Assert.Equal(0, await CountDeliveredMarksAsync(token));
    }

    /// <summary>The H13/H14 shape: the recalculation really ran (200, success, exactly one audit row
    /// for it), and neither the response nor the audit details carry the next month's pay period.</summary>
    private async Task AssertRecalculatedAndNamedNothingAsync(
        string employeeId, HttpStatusCode status, string json, string nextPeriodStart, string nextPeriodEnd)
    {
        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        using var response = JsonDocument.Parse(json);
        Assert.True(response.RootElement.GetProperty("success").GetBoolean(), json);
        var responseDates = StringLeaves(response.RootElement);
        Assert.DoesNotContain(nextPeriodStart, responseDates);
        Assert.DoesNotContain(nextPeriodEnd, responseDates);

        var auditRow = Assert.Single(await ReadCorrectionAuditDetailsAsync(employeeId));
        using var audit = JsonDocument.Parse(auditRow);
        var auditDates = StringLeaves(audit.RootElement);
        Assert.DoesNotContain(nextPeriodStart, auditDates);
        Assert.DoesNotContain(nextPeriodEnd, auditDates);
    }

    /// <summary>Every string value anywhere in a JSON document — the VALUE-level view that lets a
    /// fact assert "this date is named" without pinning the member that carries it.</summary>
    private static HashSet<string> StringLeaves(JsonElement root)
    {
        var leaves = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                        Walk(property.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Walk(item);
                    break;
                case JsonValueKind.String:
                    leaves.Add(element.GetString()!);
                    break;
            }
        }
        Walk(root);
        return leaves;
    }

    // ─── HTTP and bodies ─────────────────────────────────────────────────

    private static async Task<(HttpStatusCode Status, string Json)> PostAsync(
        WebApplicationFactory<RetroactiveCorrectionService> host, string path, object body)
    {
        var client = host.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, body.GetType()),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
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
            employeeId: "ROOT-S145",
            name: "ROOT-S145",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static EmploymentProfile Profile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    /// <summary>A calculating-route body. Hours are NOT inputs (R14): the lists are empty unless a
    /// fact is deliberately proving that a non-empty list is refused.</summary>
    private static object CalculateBody(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, IReadOnlyList<TimeEntry>? entries = null) => new
    {
        profile = Profile(employeeId),
        entries = entries ?? Array.Empty<TimeEntry>(),
        absences = Array.Empty<AbsenceEntry>(),
        periodStart,
        periodEnd,
        previousFlexBalance = 0m,
    };

    private static object RecalculateBody(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, Guid idempotencyToken,
        IReadOnlyList<TimeEntry>? entries = null) => new
    {
        profile = Profile(employeeId),
        entries = entries ?? Array.Empty<TimeEntry>(),
        absences = Array.Empty<AbsenceEntry>(),
        periodStart,
        periodEnd,
        previousFlexBalance = 0m,
        reason = "S145 host pin — correction of an exported month",
        idempotencyToken,
    };

    /// <summary>A caller-calculated result: one NORMAL_HOURS line (7.4 h) on each given date —
    /// the shape the raw routes receive (lines already computed; the route only maps them).</summary>
    private static PayrollExportRequest RawExportBody(string employeeId, params DateOnly[] dates) => new()
    {
        CalculationResult = new CalculationResult
        {
            RuleId = "NORM_CHECK_37H",
            EmployeeId = employeeId,
            Success = true,
            LineItems = dates
                .Select(d => new CalculationLineItem { TimeType = "NORMAL_HOURS", Hours = 7.4m, Rate = 0m, Date = d })
                .ToList(),
        },
        Profile = Profile(employeeId),
    };

    /// <summary>Arrangement: export a month through the raw route (which bypasses approval and
    /// records the lock with a null period id). Asserted, so a broken arrangement is not mistaken
    /// for the behaviour under test.</summary>
    private async Task ExportRawAsync(string employeeId, params DateOnly[] dates)
    {
        var (status, json) = await PostAsync(Host, "/api/payroll/export", RawExportBody(employeeId, dates));
        Assert.True(status == HttpStatusCode.OK, $"arrangement: raw export expected 200, got {(int)status}: {json}");
    }

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

    // ─── Seeding ─────────────────────────────────────────────────────────

    /// <summary>users + employee_profiles + user_agreement_codes + the organization. The employment
    /// window is both-NULL (employed the whole time) and the rows start at
    /// <paramref name="effectiveFrom"/> — default <c>0001-01-01</c>, history-covering; H8 passes the
    /// hire date, so its agreement-code row is a version-1 row dated on the hire date.</summary>
    private async Task SeedEmployeeAsync(string employeeId, DateOnly? effectiveFrom = null)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, AgreementCode, OkVersion, partTimeFraction: 1.0m, effectiveFrom: effectiveFrom);
    }

    private async Task SetEmploymentStartAsync(string employeeId, DateOnly start)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @start WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("start", start);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
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

    /// <summary>One monthly approval row in the given status (APPROVED for the month's own
    /// <c>/calculate-and-export</c> guard and for "the earlier month is approved"; EMPLOYEE_APPROVED
    /// for H4). A month with no row is simply never seeded.</summary>
    private async Task SeedApprovalAsync(string employeeId, DateOnly periodStart, DateOnly periodEnd, string status)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', @status,
                NOW(), @emp,
                CASE WHEN @status = 'APPROVED' THEN 'ROOT-S145' END,
                CASE WHEN @status = 'APPROVED' THEN NOW() END,
                @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("agreement", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>The FIRST-export record for a calendar month (<c>original_lines ==
    /// current_effective_lines</c>), written with the production manifest serializer — one
    /// NORMAL_HOURS line, with a null period id (the shape a raw-route export leaves).</summary>
    private async Task PersistExportRecordAsync(string employeeId, int year, int month)
    {
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        var lines = new[]
        {
            new PayrollExportLine
            {
                EmployeeId = employeeId,
                WageType = "SLS_0110",
                Hours = 162.8m,
                Amount = 0m,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
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
                @lines::jsonb, @lines::jsonb, @hash, 'EXPORT'
            )
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("month", month);
        cmd.Parameters.Add(new NpgsqlParameter("lines", NpgsqlDbType.Jsonb) { Value = json });
        cmd.Parameters.AddWithValue("hash", hash);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ─── Reads ───────────────────────────────────────────────────────────

    private async Task<string> ReadCurrentEffectiveLinesAsync(string employeeId, int year, int month)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT current_effective_lines::text FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "no payroll_export_records row");
        return reader.GetString(0);
    }

    /// <summary>All export records for the employee, any month — the lock rows.</summary>
    private async Task<int> CountExportRecordsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountExportRecordsAsync(string employeeId, int year, int month)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e AND year = @y AND month = @m", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountManifestsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountCorrectionEventsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested' AND stream_id LIKE @s", conn);
        cmd.Parameters.AddWithValue("s", $"retro-correction-{employeeId}-%");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>The <c>details</c> JSON of every correction audit row written for the employee.</summary>
    private async Task<List<string>> ReadCorrectionAuditDetailsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT details::text FROM audit_projection
            WHERE event_type = 'RetroactiveCorrectionRequested' AND target_resource_id = @e
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var rows = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }

    private async Task<int> CountDeliveredMarksAsync(Guid token)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t AND status = 'delivered'", conn);
        cmd.Parameters.AddWithValue("t", token);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
