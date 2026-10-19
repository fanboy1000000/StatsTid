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
/// S144 / TASK-14403, re-expected at S145 Stage B (TASK-14522-T, refinement B2) — HOST-LEVEL pins for a month
/// with a mid-month agreement-code change. The two CALCULATING endpoints now PAY such a month part by part (when
/// the change is on a Monday); the two RAW export routes refuse it with their own 422; a change in the middle
/// of a week is refused by the week check on every route.
///
/// <para>
/// <b>The story, in plain language.</b> An employee is employed all of March 2026. On Monday 16 March
/// their agreement code changes (HK → AC). Before S144 the payroll host never saw the change (it paid March
/// under one agreement), and when the planner DID refuse a month (a profile change) the refusal escaped as a
/// bare 500. S144 made every route answer 422 with <c>kind = "payroll-recalc-blocked"</c>, because the live
/// rule set could not evaluate its whole-period rules in two pieces. S145 Stage B lifted that limit on the
/// calculating routes: payroll hands the planner only the rules it runs, the month plans into two parts, and
/// <c>/recalculate</c> and <c>/calculate-and-export</c> answer 200 and pay each part under its own codes
/// (XB1, XB2). The raw routes (<c>/export</c>, <c>/export-period</c>) take lines the caller already
/// calculated and map every line under the request's ONE profile, so they cannot pay a month with two parts
/// correctly: they refuse it with the single-profile-route 422 (<c>reason = split-month-single-profile-route</c>,
/// RB1, RB2). An agreement change on a Wednesday is also in the middle of a Monday–Sunday week, which one
/// agreement cannot judge: every route answers the week check's 422 (IB1). All bodies carry no date and no
/// employee id.
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
/// March 2026 opens on a Sunday, so its opening week (23 February – 1 March) holds February's EMPLOYED days:
/// the calculating-route facts seed February APPROVED (the months-in-order check). Since S145 B2 XB1 and XB2
/// also seed recorded weekday hours for March (7.4 h on each weekday from 2 to 31 March): the stub engine
/// answers one NORMAL_HOURS line per recorded entry, and /calculate-and-export writes an export record only
/// when the calculation produced lines.
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

    // The fixed 503 sentence (Payroll/Program.cs, RulesUnavailable), pinned word for word (S145 R38 /
    // N-c4-1). "Nothing was calculated, written or exported" is a statement about payroll data; see the
    // wording note at the (d) block (N-c4-4).
    private const string RulesUnavailableSentence =
        "The rule set could not be obtained from the Rule Engine, so the period could not be checked. " +
        "Nothing was calculated, written or exported. Retry when the Rule Engine is reachable.";

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

        // The LIVE rule set (the registry's full classification set; it carries three AlignedWindow rules).
        // S145 B2: payroll hands the planner only the six rules it evaluates, so the live set no longer
        // refuses a split month at the planner — the raw routes refuse it by Q6 (a), and the week check
        // answers an agreement change in the middle of a week.
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
    /// <b>S145 B2: was <c>Recalculate_MidMonthAgreementCodeChange_Returns422_RedactedProblem_LinesUnchanged</c>.</b>
    /// (a) <c>POST /api/payroll/recalculate</c> as Global Admin, for an exported March with an
    /// agreement-code change recorded on MONDAY 16 March (February APPROVED, because March's opening week
    /// begins on 23 February) → <b>200</b>: the correction commits — <c>current_effective_lines</c> changed,
    /// one <c>RetroactiveCorrectionRequested</c> event, one manifest. No per-line wage-type assertion here
    /// (the per-part codes are pinned at service level, <c>RecalcBlockedLiveRulesetTests</c>).
    ///
    /// <para>
    /// This fact also proves the host's DI wiring of
    /// <see cref="PeriodCalculationService"/>'s <c>userAgreementCodeRepo</c>: with a null repository
    /// the planner sees one segment and the manifest's segment count is 1, not 2.
    /// </para>
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). MB-7 (TASK-14528: the
    /// evaluated-set filter disabled, so the planner sees NORM_CHECK_WEEKLY as AlignedWindow again): the
    /// planner refuses the split and the endpoint answers <b>422</b>, tripping the 200 assertion (and
    /// <c>current_effective_lines</c> stays unchanged). Mutation M-14 (hydration of the agreement-code dates
    /// skipped): one segment, so the manifest's two-segment assertion trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recalculate_MondayAgreementCodeChange16Mar_FebruaryApproved_Recalculates_200() // S145 B2: was Recalculate_MidMonthAgreementCodeChange_Returns422_RedactedProblem_LinesUnchanged
    {
        const string employeeId = "EMP-S144-HOST-RECALC";
        await SeedEmployeeAsync(employeeId);
        await SeedMonthApprovalAsync(employeeId, Feb01, Feb28, OkVersion); // S145 B2: March's opening week holds February's days
        await SeedMarchWeekdayHoursAsync(employeeId); // S145 B2: recorded hours, so the month has lines
        await PersistFirstExportRecordAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId);

        var body = new
        {
            profile = Profile(employeeId),
            periodStart = Mar01,
            periodEnd = Mar31,
            previousFlexBalance = 0m,
            reason = "S144 host pin — agreement change recorded after export",
            idempotencyToken = Guid.NewGuid(),
        };

        var response = await PostAsGlobalAdminAsync("/api/payroll/recalculate", body);
        var json = await response.Content.ReadAsStringAsync();

        // S145 B2: the correction commits (it used to answer the redacted 422 and write nothing).
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"expected 200, got {(int)response.StatusCode}: {json}");
        Assert.NotEqual(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId)); // S145 B2: the baseline moved
        Assert.Equal(1, await CountCorrectionEventsAsync(employeeId)); // S145 B2
        var manifest = await LoadSingleManifestAsync(employeeId); // S145 B2: exactly one manifest, written by the recalculation
        Assert.Equal(2, manifest.SegmentCount);
        Assert.Contains("AgreementCodeChange", manifest.BoundaryCauseSummary);
    }

    // ═════════════════════════════════════════════════════════════════════
    // (b) /calculate-and-export
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S145 B2: was <c>CalculateAndExport_MidMonthAgreementCodeChange_Returns422_NoExportRecord_NoManifest</c>.</b>
    /// (b) <c>POST /api/payroll/calculate-and-export</c> as Global Admin, for an APPROVED March (February
    /// APPROVED too) with an agreement-code change on MONDAY 16 March → <b>200</b>: ONE
    /// <c>payroll_export_records</c> row, and ONE <c>segment_manifests</c> row with two EMPLOYED segments and
    /// <c>AgreementCodeChange</c>. No per-line wage-type assertion here (the per-part codes are pinned at
    /// service level, <c>RecalcBlockedLiveRulesetTests</c>). This fact runs on <see cref="DeliveringFactory"/>
    /// (S145 B2, R-58): the shared host's stub answers the post-commit delivery POST with 404, which would turn
    /// the committed export into a 422 even though the month was paid; the delivering host answers 200, so a 200
    /// means what it says. <i>Plain language: after a successful export, payroll posts the file to the mock
    /// payroll system; the shared test host answers that post with "not found", which the route reports as a
    /// 422. The delivering host answers "OK".</i>
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). MB-7 (the evaluated-set filter
    /// disabled): the planner refuses the split month, the route answers <b>422</b> with the planner's body, the
    /// 200 assertion trips (and no record or manifest exists). Mutation M-14 (hydration skipped): one segment, so
    /// the manifest's two-segment assertion trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_MondayAgreementCodeChange16Mar_FebruaryApproved_Pays_200() // S145 B2: was CalculateAndExport_MidMonthAgreementCodeChange_Returns422_NoExportRecord_NoManifest
    {
        const string employeeId = "EMP-S144-HOST-EXPORT";
        await SeedEmployeeAsync(employeeId);
        await SeedMonthApprovalAsync(employeeId, Feb01, Feb28, OkVersion); // S145 B2: March's opening week holds February's days
        await SeedApprovedPeriodAsync(employeeId);
        await SeedMarchWeekdayHoursAsync(employeeId); // S145 B2: recorded hours, so the month has lines to export
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar16);

        var body = new
        {
            profile = Profile(employeeId),
            periodStart = Mar01,
            periodEnd = Mar31,
            previousFlexBalance = 0m,
        };

        // S145 B2 (R-58): the delivering host, so a committed export answers 200, not a look-alike 422.
        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/calculate-and-export", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"expected 200, got {(int)response.StatusCode}: {json}");
        Assert.Equal(1, await CountAsync("payroll_export_records", employeeId)); // S145 B2
        Assert.Equal(1, await CountAsync("segment_manifests", employeeId)); // S145 B2
        var manifest = await LoadSingleManifestAsync(employeeId);
        Assert.Equal(2, manifest.SegmentCount);
        Assert.Contains("AgreementCodeChange", manifest.BoundaryCauseSummary);
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
    /// (c1) <b>RB1.</b> <c>POST /api/payroll/export</c> as Global Admin, with a caller-calculated March-2026
    /// <c>CalculationResult</c> (NORMAL_HOURS lines on 2 March and 20 March, either side of the
    /// agreement-code change on Monday 16 March) and the employee's profile → <b>422</b>,
    /// <c>kind = payroll-recalc-blocked</c>, <b>S145 B2: <c>reason = split-month-single-profile-route</c></b>
    /// (the raw routes map every line under the request's one profile, so they cannot pay a month with two
    /// parts — Q6 (a)), <c>employedSegmentCount = 2</c>, causes <c>["AgreementCodeChange"]</c>, no
    /// <c>ruleId</c>, no date, no employee id; NO <c>payroll_export_records</c> row for the employee's
    /// March. (Name kept; the body changed from the planner's to the single-profile body.)
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). MB-5 (TASK-14528: the Q6 (a)
    /// check removed from <c>EnsurePeriodCalculableAsync</c>): the guard plans two segments, finds nothing to
    /// refuse (the planner no longer refuses, the change is on a Monday), the route maps and exports, and answers
    /// <b>200</b>; the 422 assertion trips (and a March record exists). MB-7 (the evaluated-set filter
    /// disabled): the planner refuses first with ITS body, which carries a <c>ruleId</c> and no
    /// <c>reason</c>, so the <c>reason</c> assertion trips. Mutation M-14 (hydration nulled): the guard plans
    /// ONE segment and is blind → <b>200</b>. The guard moved AFTER <c>ExportAsync</c>: the March record is
    /// already committed, so the zero-row assertion trips.
    /// </para>
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
        AssertSingleProfileRouteRefusal(json, employeeId); // S145 B2: was AssertRedactedProblem (the planner's body)

        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, Month));
    }

    /// <summary>
    /// (c2) <b>RB2.</b> <c>POST /api/payroll/export-period</c> as Global Admin, with TWO caller-calculated
    /// results: February 2026 (no change inside it — it would export on its own, see (c3)) and
    /// March 2026 (the agreement-code change on Monday 16 March) → <b>422</b> with the S145 B2 single-profile
    /// body (<c>reason = split-month-single-profile-route</c>), and NO export record for EITHER month.
    /// All-or-nothing: the guard runs over every result's months before the first one is mapped, so the clean
    /// February is not written either. (Name kept; the body changed from the planner's.)
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). MB-5 (the Q6 (a) check removed):
    /// the guard finds nothing to refuse in March → both months export → <b>200</b>; the 422 assertion trips
    /// (and both records exist). MB-7 (the evaluated-set filter disabled): the planner refuses March first with
    /// its own body (a <c>ruleId</c>, no <c>reason</c>), so the <c>reason</c> assertion trips. Mutation M-14:
    /// the guard is blind to March → <b>200</b>. The guard moved AFTER <c>ExportAsync</c>: February and March
    /// records are already committed — the zero-row assertions trip.
    /// </para>
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
        AssertSingleProfileRouteRefusal(json, employeeId); // S145 B2: was AssertRedactedProblem (the planner's body)

        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, 2));
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, Year, Month));
    }

    /// <summary>
    /// (c3) No-regression: the SAME seed (agreement-code change on 16 March), but a February-2026
    /// result — no interior change in February — through <c>POST /api/payroll/export</c> →
    /// <b>200</b> and exactly one February <c>payroll_export_records</c> row. The guard refuses only
    /// the months a raw route cannot export correctly (since S145 B2: a split month, Q6 (a), or an
    /// agreement change inside a settled week); it does not turn every raw export into a 422.
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
    // before anything is written. (Since S145 B2 payroll hands the planner only the rules it
    // evaluates, none of which refuses a split, and the raw routes refuse a split month by their own
    // Q6 (a) check; so an empty set would now answer that 422, not export. The 503 still matters: an
    // unknown set must never be planned and merged with as though it were an empty one.)
    //
    // WORDING NOTE (S145 R38 / N-c4-4). "Nothing written" in this block, and the 503's own sentence
    // ("Nothing was calculated, written or exported"), mean PAYROLL DATA: no export record, no manifest,
    // no correction event, no baseline change — which is what every fact below asserts. It is not a
    // claim about the audit trail: the refused request itself is still recorded by the host's audit
    // middleware (audit_log), by design. Whether the sentence should say so is a wording question the
    // owner has not ruled; the sentence is pinned word for word in AssertRulesUnavailable (N-c4-1), so
    // a later wording change moves that pin on purpose.
    //
    // These facts run on UnavailableRulesFactory(): the REAL HttpRuleClassificationProvider over a
    // client whose every call answers 503 (so the real provider throws inside the real host, and the
    // real handler maps it), plus the (c) delivering handler — so that a provider reverted to "empty"
    // answers something other than the 503: before S145 B2 a clean 200 export, since B2 the Q6 (a)
    // single-profile 422 for the split March. Either way the 503 assertion trips.

    /// <summary>
    /// (d1) <c>POST /api/payroll/export</c> with the (c1) seed and body, while the Rule Engine's
    /// classifications endpoint answers 503 → <b>503</b>, <c>kind = payroll-rules-unavailable</c>,
    /// <c>success = false</c>, no ISO date and no employee id in the body; NO March export record.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified):
    /// (1) the provider reverts to returning an empty set on failure → planning no longer fails, and
    /// the guard's Q6 (a) check answers the split March with the single-profile <b>422</b> (before
    /// S145 B2: a 200 plus a March record); the 503 assertion trips.
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
    /// (1) the provider reverts to returning an empty set on failure → planning no longer fails, and
    /// the guard's Q6 (a) check answers the split March with the single-profile <b>422</b> (before
    /// S145 B2: both months exported, a 200 plus two records); the 503 assertion trips.
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
        // N-c4-1 (S144 Step 7a cycle 4, ruled R38): the sentence itself, word for word — no longer
        // verified by inspection only. An upstream status or body leaking into it would trip this.
        Assert.Equal(RulesUnavailableSentence, root.GetProperty("error").GetString());
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

    /// <summary>The fixed sentence of the single-profile-route variant (S145 B2, Q6 (a), R-76), word for word.
    /// It carries no date, id or digit.</summary>
    private const string SingleProfileRouteSentence =
        "This period cannot be exported through this route: a change inside the period splits it into parts " +
        "with different profiles, and this route maps every line under a single profile. Use the calculating " +
        "routes (recalculate, or calculate-and-export) to pay the period.";

    private const string SingleProfileRouteReason = "split-month-single-profile-route";

    /// <summary>S145 B2: the raw routes' Q6 (a) 422 body — <c>kind</c> payroll-recalc-blocked, <c>reason</c>
    /// split-month-single-profile-route, <c>employedSegmentCount</c> 2, causes <c>["AgreementCodeChange"]</c>,
    /// the fixed sentence, no <c>ruleId</c>, no date (in any rendering) and no employee id.</summary>
    private static void AssertSingleProfileRouteRefusal(string json, string employeeId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(Kind, root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(SingleProfileRouteReason, root.GetProperty("reason").GetString());
        Assert.Equal(2, root.GetProperty("employedSegmentCount").GetInt32());
        Assert.Equal(new[] { "AgreementCodeChange" },
            root.GetProperty("interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(SingleProfileRouteSentence, root.GetProperty("error").GetString());
        Assert.True(
            !root.TryGetProperty("ruleId", out var ruleId) || ruleId.ValueKind == JsonValueKind.Null,
            "the single-profile-route variant carries no ruleId (it is not a rule's refusal)");

        Assert.DoesNotMatch(IsoDatePattern, json);
        foreach (var date in new[] { Mar01, Mar16, Mar31 })
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

    private long _outboxSeq = 1000;

    /// <summary>S145 B2: recorded hours for March 2026 — 7.4 h on each weekday from 2 to 31 March, as RAW
    /// <c>time_entries_projection</c> rows (the shape <c>PayrollHostRealEngineTests</c> seeds). Payroll reads
    /// the employee's hours itself; the stub engine answers one NORMAL_HOURS line per recorded entry, and
    /// <c>/calculate-and-export</c> writes an export record only when the calculation produced lines.</summary>
    private async Task SeedMarchWeekdayHoursAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        for (var day = new DateOnly(2026, 3, 2); day <= Mar31; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, 7.4, @agreement, @ok, FALSE, NOW(), @outbox)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", day);
            cmd.Parameters.AddWithValue("agreement", InitialAgreementCode);
            cmd.Parameters.AddWithValue("ok", OkVersion);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
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

    private sealed record ManifestRow(int SegmentCount, IReadOnlyList<string> BoundaryCauseSummary);

    /// <summary>S145 B2: the employee's ONE <c>segment_manifests</c> row (asserts there is exactly one).</summary>
    private async Task<ManifestRow> LoadSingleManifestAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT jsonb_array_length(segments_jsonb), boundary_cause_summary
            FROM segment_manifests
            WHERE employee_id = @e
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no segment_manifests row for {employeeId}");
        var row = new ManifestRow(reader.GetInt32(0), ((string[])reader.GetValue(1)).ToList());
        Assert.False(await reader.ReadAsync(), $"more than one segment_manifests row for {employeeId}");
        return row;
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

    // ═════════════════════════════════════════════════════════════════════
    // (e) S145 / TASK-14503b — the agreement-change residual on every payroll route (Q5 (c))
    // ═════════════════════════════════════════════════════════════════════
    //
    // In plain language. S144 refused a month with an agreement-code change INSIDE the month: the
    // planner saw it. (Since S145 B2 the planner no longer refuses it: an interior Monday change is
    // paid part by part, and an interior mid-week change is answered by this same week check, IB1.)
    // Two cases slipped past the planner, because the agreement read starts at the
    // 1st of the month. (1) A change dated ON a non-Monday 1st: 1 September 2026 is a Tuesday, so
    // it is not "interior", yet it falls inside the week 31 August – 6 September, which September
    // settles. (2) A change in the PREVIOUS month's last days that the month's first week includes:
    // 29 September falls inside the week 28 September – 4 October, which October settles. One week
    // cannot be judged by two agreements, so from S145 both are refused on ALL FOUR routes (ruling
    // Q5 (c)) with a second 422 variant that is told apart from the planner's by `reason`:
    //   { success:false, error:<fixed sentence>, kind:"payroll-recalc-blocked",
    //     reason:"agreement-change-mid-week", interiorBoundaryCauses:["AgreementCodeChange"] }
    // — no ruleId (it is not a rule's refusal), no date, no employee id — and nothing is written:
    // no export record (the lock), no manifest; for /recalculate also no baseline change, no
    // correction event, no correction audit row and no idempotency marker.
    //
    // Fixture rules (plan § "Fixture conditions the table relies on"): the calculating-route and
    // recalculation facts seed the previous month APPROVED (C-H1), so the months-in-order 409 can
    // never be what answers; the raw routes do not read approval at all. Bodies carry no hours
    // (after TASK-14508 hours in a body answer 400, and TASK-14508-T migrates only the bodies of
    // the S144 facts above). The profile in the body is the OLD agreement (HK) unless a fact says
    // otherwise (C-H3). Every month here is after the 1 April 2026 OK transition, so the profile is
    // OK26, except AR10 (March, OK24, like the S144 facts it extends).
    //
    // Red conditions, shared by the eight route facts (Docker-gated — cannot run locally;
    // CI-verified). Each fails today with `expected 422, got 200` (before TASK-14512 the agreement
    // read starts at the 1st, so neither case is seen and the route pays or exports). The named
    // mutations (plan § "RED evidence"):
    //   * the two calculating routes (/recalculate, /calculate-and-export): MD-26 (the week-check call
    //     removed from the per-plan lane → 200), and MD-27 (the agreement read keeps
    //     `afterExclusive: periodStart`, blind to both dates → 200; in run EA-2, where MD-21 also
    //     nulls the approval reader, the never-silent throw escapes instead of the 200);
    //   * the two raw routes (/export, /export-period): MD-25 (the raw-route guard skips the week
    //     predicate → 200), and MD-27 as above (→ 200).

    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar18 = new(2026, 3, 18);
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);
    private static readonly DateOnly Aug01 = new(2026, 8, 1);
    private static readonly DateOnly Aug10 = new(2026, 8, 10);
    private static readonly DateOnly Aug20 = new(2026, 8, 20);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep02 = new(2026, 9, 2);
    private static readonly DateOnly Sep15 = new(2026, 9, 15);
    private static readonly DateOnly Sep29 = new(2026, 9, 29);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct05 = new(2026, 10, 5);
    private static readonly DateOnly Oct20 = new(2026, 10, 20);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);
    private static readonly DateOnly Nov03 = new(2026, 11, 3);
    private static readonly DateOnly Nov17 = new(2026, 11, 17);
    private const string MidWeekReason = "agreement-change-mid-week";

    /// <summary>
    /// The calendar premise the AR facts share, asserted ONCE here rather than left to the prose above: the
    /// 1st-of-month change dates are a Tuesday (AR1-AR4) and a Monday (AR9), the late-month change is a
    /// Tuesday inside the week that starts on Monday 28 September (AR5-AR8), and AR10's March change is a
    /// Wednesday. If a date moved onto a different weekday, the fact built on it would stop testing what it
    /// says (a mid-week change versus a week-start one) while still passing or failing for another reason.
    /// Pure date arithmetic; it touches no database.
    /// </summary>
    [Fact]
    public void AgreementChangeDates_AreTheWeekdaysTheArFactsRelyOn()
    {
        Assert.Multiple(
            () => Assert.Equal(DayOfWeek.Tuesday, Sep01.DayOfWeek),
            () => Assert.Equal(DayOfWeek.Tuesday, Sep29.DayOfWeek),
            () => Assert.Equal(DayOfWeek.Monday, Sep29.AddDays(-1).DayOfWeek),
            () => Assert.Equal(DayOfWeek.Monday, Jun01.DayOfWeek),
            () => Assert.Equal(DayOfWeek.Wednesday, Mar18.DayOfWeek));
    }

    // ── 1 September 2026 (Tuesday): the change is ON the 1st, inside September's opening week ──

    /// <summary>
    /// AR1. <c>POST /api/payroll/recalculate</c> for exported September 2026 with the agreement HK → AC
    /// effective Tuesday 1 September (August APPROVED) → <b>422</b>, the week check's variant
    /// (<c>reason = agreement-change-mid-week</c>, no <c>ruleId</c>, no date, no employee id); the
    /// export baseline is unchanged and no correction event, audit row, manifest or marker is written.
    /// Red conditions: see the section note (MD-26, MD-27).
    /// </summary>
    [Fact]
    public async Task Recalculate_AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing()
    {
        const string employeeId = "EMP-S145-AR1";
        var token = Guid.NewGuid();
        await SeedEmployeeOk26Async(employeeId);
        await SeedMonthApprovalAsync(employeeId, Aug01, Aug31, "OK26");
        await PersistMonthExportRecordAsync(employeeId, 2026, 9);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Sep01);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId, 2026, 9);

        var response = await PostAsGlobalAdminAsync("/api/payroll/recalculate",
            S145RecalculateBody(employeeId, Sep01, Sep30, token));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep01, Sep30);
        await AssertRecalculationWroteNothingAsync(employeeId, token, linesBefore, 2026, 9);
    }

    /// <summary>
    /// AR2. <c>POST /api/payroll/calculate-and-export</c> for an APPROVED September 2026 (August
    /// APPROVED too) with HK → AC effective Tuesday 1 September → <b>422</b>, the week check's variant;
    /// no export record and no manifest. Red conditions: see the section note (MD-26, MD-27).
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing()
    {
        const string employeeId = "EMP-S145-AR2";
        await SeedEmployeeOk26Async(employeeId);
        await SeedMonthApprovalAsync(employeeId, Aug01, Aug31, "OK26");
        await SeedMonthApprovalAsync(employeeId, Sep01, Sep30, "OK26");
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Sep01);

        var response = await PostAsGlobalAdminAsync("/api/payroll/calculate-and-export",
            S145CalculateBody(employeeId, Sep01, Sep30));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep01, Sep30);
        Assert.Equal(0, await CountAsync("payroll_export_records", employeeId));
        Assert.Equal(0, await CountAsync("segment_manifests", employeeId));
    }

    /// <summary>
    /// AR3. The raw <c>POST /api/payroll/export</c> with a caller-calculated September 2026 result
    /// (lines on 2 and 15 September) and HK → AC effective Tuesday 1 September → <b>422</b>, the week
    /// check's variant; NO September export record (the route bypasses approval by design, but it may
    /// not export what the calculating routes refuse — QUAL-183). Red conditions: see the section
    /// note (MD-25, MD-27).
    /// </summary>
    [Fact]
    public async Task Export_AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing()
    {
        const string employeeId = "EMP-S145-AR3";
        await SeedEmployeeOk26Async(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Sep01);

        var body = new PayrollExportRequest
        {
            CalculationResult = RawResult(employeeId, Sep02, Sep15),
            Profile = S145Profile(employeeId, InitialAgreementCode),
        };

        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/export", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep01, Sep30);
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, 2026, 9));
    }

    /// <summary>
    /// AR4. The raw <c>POST /api/payroll/export-period</c> with TWO results — August 2026 (no change
    /// near it: it would export on its own) and September 2026 (HK → AC effective Tuesday 1 September)
    /// → <b>422</b>, the week check's variant, and NO export record for EITHER month: the guard runs
    /// over every result's months, in date order, before the first one is mapped. Red conditions: see
    /// the section note (MD-25, MD-27).
    /// </summary>
    [Fact]
    public async Task ExportPeriod_AgreementChangeTue1Sep_Returns422_AgreementChangeMidWeek_WritesNothing()
    {
        const string employeeId = "EMP-S145-AR4";
        await SeedEmployeeOk26Async(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Sep01);

        var body = new PayrollPeriodExportRequest
        {
            CalculationResults = new List<CalculationResult>
            {
                RawResult(employeeId, Aug10, Aug20),
                RawResult(employeeId, Sep02, Sep15),
            },
            Profile = S145Profile(employeeId, InitialAgreementCode),
        };

        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/export-period", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep01, Sep30);
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, 2026, 8));
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, 2026, 9));
    }

    // ── 29 September 2026 (Tuesday): the change is in the PREVIOUS month, inside October's week ──
    //
    // September itself is refused by the planner (the change is interior to it), which is the S144
    // behaviour and not this group's subject; these four facts calculate OCTOBER, whose first week
    // (28 September – 4 October) includes the change as a context day.

    /// <summary>
    /// AR5. <c>POST /api/payroll/recalculate</c> for exported October 2026 (September APPROVED) with
    /// HK → AC effective Tuesday 29 September → <b>422</b>, the week check's variant: October settles
    /// the week 28 September – 4 October, and 29 September is a non-Monday inside it. Nothing is
    /// written. Red conditions: see the section note (MD-26, MD-27).
    /// </summary>
    [Fact]
    public async Task Recalculate_AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing()
    {
        const string employeeId = "EMP-S145-AR5";
        var token = Guid.NewGuid();
        await SeedEmployeeOk26Async(employeeId);
        await SeedMonthApprovalAsync(employeeId, Sep01, Sep30, "OK26");
        await PersistMonthExportRecordAsync(employeeId, 2026, 10);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Sep29);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId, 2026, 10);

        var response = await PostAsGlobalAdminAsync("/api/payroll/recalculate",
            S145RecalculateBody(employeeId, Oct01, Oct31, token));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep29, Oct01, Oct31);
        await AssertRecalculationWroteNothingAsync(employeeId, token, linesBefore, 2026, 10);
    }

    /// <summary>
    /// AR6. <c>POST /api/payroll/calculate-and-export</c> for an APPROVED October 2026 (September
    /// APPROVED too) with HK → AC effective Tuesday 29 September → <b>422</b>, the week check's
    /// variant; no export record and no manifest. Red conditions: see the section note (MD-26,
    /// MD-27).
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing()
    {
        const string employeeId = "EMP-S145-AR6";
        await SeedEmployeeOk26Async(employeeId);
        await SeedMonthApprovalAsync(employeeId, Sep01, Sep30, "OK26");
        await SeedMonthApprovalAsync(employeeId, Oct01, Oct31, "OK26");
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Sep29);

        var response = await PostAsGlobalAdminAsync("/api/payroll/calculate-and-export",
            S145CalculateBody(employeeId, Oct01, Oct31));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep29, Oct01, Oct31);
        Assert.Equal(0, await CountAsync("payroll_export_records", employeeId));
        Assert.Equal(0, await CountAsync("segment_manifests", employeeId));
    }

    /// <summary>
    /// AR7. The raw <c>POST /api/payroll/export</c> with a caller-calculated October 2026 result (lines
    /// on 5 and 20 October) and HK → AC effective Tuesday 29 September → <b>422</b>, the week check's
    /// variant; NO October export record. Red conditions: see the section note (MD-25, MD-27).
    /// </summary>
    [Fact]
    public async Task Export_AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing()
    {
        const string employeeId = "EMP-S145-AR7";
        await SeedEmployeeOk26Async(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Sep29);

        var body = new PayrollExportRequest
        {
            CalculationResult = RawResult(employeeId, Oct05, Oct20),
            Profile = S145Profile(employeeId, InitialAgreementCode),
        };

        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/export", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep29, Oct01, Oct31);
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, 2026, 10));
    }

    /// <summary>
    /// AR8. The raw <c>POST /api/payroll/export-period</c> with TWO results — October 2026 (the change
    /// of 29 September is a context day of its first week) and November 2026 (clean) → <b>422</b>, the
    /// week check's variant, and NO export record for EITHER month (all-or-nothing: October is checked
    /// first, in date order, and refuses the whole call). Red conditions: see the section note (MD-25,
    /// MD-27).
    /// </summary>
    [Fact]
    public async Task ExportPeriod_AgreementChangeTue29Sep_OctoberReturns422_AgreementChangeMidWeek_WritesNothing()
    {
        const string employeeId = "EMP-S145-AR8";
        await SeedEmployeeOk26Async(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Sep29);

        var body = new PayrollPeriodExportRequest
        {
            CalculationResults = new List<CalculationResult>
            {
                RawResult(employeeId, Oct05, Oct20),
                RawResult(employeeId, Nov03, Nov17),
            },
            Profile = S145Profile(employeeId, InitialAgreementCode),
        };

        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/export-period", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Sep29, Oct01, Oct31);
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, 2026, 10));
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, 2026, 11));
    }

    // ── Two guards: what the new check must NOT refuse, and what it must not take over ──

    /// <summary>
    /// AR9 <b>(guard)</b>. HK → AC effective MONDAY 1 June 2026, June APPROVED, the request carrying AC
    /// (C-H3: the agreement in force on 1 June) → <b>200</b>. A change dated on a Monday starts a week;
    /// it is not "in the middle of" one, and June's first week (1–7 June) holds no May day. A week
    /// check that refused every change on the 1st would stop this month paying.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): passes today (the agreement read
    /// starts at the 1st and a change ON the 1st is not interior), and carries no MD-id. It trips the
    /// day the week check over-refuses — treats a Monday change as mid-week, or counts the 1st as
    /// interior — by answering <b>422</b>; <c>Assert.True(status == 200)</c> trips.
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_AgreementChangeMonday1June_Pays_200()
    {
        const string employeeId = "EMP-S145-AR9";
        await SeedEmployeeOk26Async(employeeId);
        await SeedMonthApprovalAsync(employeeId, Jun01, Jun30, "OK26");
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Jun01);

        var response = await PostAsGlobalAdminAsync(DeliveringFactory(), "/api/payroll/calculate-and-export",
            S145CalculateBody(employeeId, Jun01, Jun30, SuccessorAgreementCode));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"expected 200, got {(int)response.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean(), json);
    }

    /// <summary>
    /// AR10 / <b>IB1</b> <b>(guard)</b>. <b>S145 B2: was
    /// <c>CalculateAndExport_AgreementChangeWed18Mar_StillPlanner422_WithRuleId</c>.</b> HK → AC effective
    /// WEDNESDAY 18 March 2026 (inside March, and in the middle of a week too), February and March APPROVED →
    /// <b>422</b>, the WEEK CHECK's variant (<c>reason = agreement-change-mid-week</c>, no <c>ruleId</c>, no
    /// date, no employee id), and nothing written. The planner no longer refuses this month (payroll hands it
    /// only the rules it evaluates, so a split month plans), so the week check, which runs over every settled
    /// week, answers: the interior residual of Stage B (refinement B2). It keeps the calculating routes and the
    /// raw routes in agreement ("one predicate, three consumers"): the change is a Wednesday, so no route can
    /// judge that week under one agreement.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated — cannot run locally; CI-verified). MB-4 (TASK-14528: the week check
    /// ignores interior dates): the check finds nothing in the month, the route calculates and answers
    /// <b>200</b>, so the 422 assertion trips. (A manifest is written, but no export record: IB1 seeds no
    /// recorded hours, so the calculation has no lines and the route skips the export; R-119.) MB-7 (the evaluated-set
    /// filter disabled): the planner refuses first with ITS body, which has a <c>ruleId</c> and no
    /// <c>reason</c> — <c>AssertWeekCheckRefusal</c>'s <c>reason</c> assertion trips (C-B4).
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_AgreementChangeWed18Mar_Returns422_AgreementChangeMidWeek_NoRuleId() // S145 B2: was CalculateAndExport_AgreementChangeWed18Mar_StillPlanner422_WithRuleId
    {
        const string employeeId = "EMP-S145-AR10";
        await SeedEmployeeAsync(employeeId);
        await SeedMonthApprovalAsync(employeeId, Feb01, Feb28, OkVersion);
        await SeedApprovedPeriodAsync(employeeId);
        await SupersedeAgreementCodeAsync(employeeId, SuccessorAgreementCode, Mar18);

        var response = await PostAsGlobalAdminAsync("/api/payroll/calculate-and-export",
            S145CalculateBody(employeeId, Mar01, Mar31, InitialAgreementCode, OkVersion));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        // S145 B2: the week variant (reason agreement-change-mid-week, no ruleId, no date, no employee id).
        AssertWeekCheckRefusal(json, employeeId, Mar18, Mar01, Mar31);
        Assert.Equal(0, await CountAsync("payroll_export_records", employeeId));
        Assert.Equal(0, await CountAsync("segment_manifests", employeeId));
    }

    // ── S145 helpers (new; the S144 helpers above are March-bound and are left untouched) ──

    /// <summary>The week check's 422 variant: <c>kind</c> payroll-recalc-blocked, <c>reason</c>
    /// agreement-change-mid-week, cause <c>AgreementCodeChange</c>, no <c>ruleId</c>, no date (in any
    /// rendering) and no employee id. (Copied, not shared, with <c>PayrollHostWeeklyTests</c>: the two
    /// files may not share a helper.)</summary>
    private static void AssertWeekCheckRefusal(string json, string employeeId, params DateOnly[] datesThatMustNotLeak)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(Kind, root.GetProperty("kind").GetString());
        Assert.Equal(MidWeekReason, root.GetProperty("reason").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
        Assert.Equal(new[] { "AgreementCodeChange" },
            root.GetProperty("interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.True(
            !root.TryGetProperty("ruleId", out var ruleId) || ruleId.ValueKind == JsonValueKind.Null,
            "the week check's variant carries no ruleId (it is not a rule's refusal)");

        Assert.DoesNotMatch(IsoDatePattern, json);
        foreach (var date in datesThatMustNotLeak)
        {
            Assert.DoesNotContain(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), json);
            Assert.DoesNotContain(date.ToString(), json);
            Assert.DoesNotContain(date.ToString(CultureInfo.InvariantCulture), json);
        }
        Assert.DoesNotContain(employeeId, json);
    }

    /// <summary>"Writes nothing" for a refused <c>/recalculate</c>: the export baseline is unchanged
    /// and there is no correction event, no correction audit row, no manifest and no idempotency
    /// marker. (The refused request itself is still audited by the host's audit middleware — by
    /// design, and not asserted either way.)</summary>
    private async Task AssertRecalculationWroteNothingAsync(
        string employeeId, Guid token, string linesBefore, int year, int month)
    {
        Assert.Equal(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId, year, month));
        Assert.Equal(0, await CountCorrectionEventsAsync(employeeId));
        Assert.Equal(0, await CountCorrectionAuditRowsAsync(employeeId));
        Assert.Equal(0, await CountAsync("segment_manifests", employeeId));
        Assert.Equal(0, await CountDeliveredMarksAsync(token));
    }

    private static EmploymentProfile S145Profile(string employeeId, string agreementCode, string okVersion = "OK26") => new()
    {
        EmployeeId = employeeId,
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    /// <summary>A calculating-route body with NO hours (R14).</summary>
    private static object S145CalculateBody(
        string employeeId, DateOnly periodStart, DateOnly periodEnd,
        string agreementCode = InitialAgreementCode, string okVersion = "OK26") => new
    {
        profile = S145Profile(employeeId, agreementCode, okVersion),
        entries = Array.Empty<TimeEntry>(),
        absences = Array.Empty<AbsenceEntry>(),
        periodStart,
        periodEnd,
        previousFlexBalance = 0m,
    };

    private static object S145RecalculateBody(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, Guid idempotencyToken) => new
    {
        profile = S145Profile(employeeId, InitialAgreementCode),
        entries = Array.Empty<TimeEntry>(),
        absences = Array.Empty<AbsenceEntry>(),
        periodStart,
        periodEnd,
        previousFlexBalance = 0m,
        reason = "S145 host pin — correction of an exported month",
        idempotencyToken,
    };

    /// <summary>As <see cref="SeedEmployeeAsync"/>, but the cached OK version is OK26 (the months of
    /// this section are after the 1 April 2026 transition).</summary>
    private async Task SeedEmployeeOk26Async(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, InitialAgreementCode, "OK26", partTimeFraction: 1.0m);
    }

    /// <summary>An APPROVED monthly approval row for any month — the month's own approval (the
    /// <c>/calculate-and-export</c> 403 guard) or "the previous month is APPROVED" (C-H1). The row's
    /// <c>ok_version</c> label is passed in and matches the profile's OK version (a fixture label; no
    /// assertion reads it).</summary>
    private async Task SeedMonthApprovalAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, string okVersion)
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
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        cmd.Parameters.AddWithValue("agreement", InitialAgreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>The FIRST-export record for a calendar month (<c>original_lines ==
    /// current_effective_lines</c>), written with the production manifest serializer — one
    /// NORMAL_HOURS line, null period id (the shape a raw-route export leaves). The S144
    /// <c>PersistFirstExportRecordAsync</c> is March-bound.</summary>
    private async Task PersistMonthExportRecordAsync(string employeeId, int year, int month)
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
                OkVersion = "OK26",
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

    private async Task<int> CountCorrectionEventsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested' AND stream_id LIKE @s", conn);
        cmd.Parameters.AddWithValue("s", $"retro-correction-{employeeId}-%");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountCorrectionAuditRowsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'RetroactiveCorrectionRequested' AND target_resource_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
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
