using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
/// S145 Stage B / TASK-14524 - HOST-LEVEL pins for the Payroll host once a split month is payable (R50: a new
/// file, because <c>PayrollHostRecalcBlockedTests</c> belongs to TASK-14522-T). Four facts: SP1 (the positive,
/// real-engine split month), IB3 (the interior residual), RB3 and RB4 (the raw export routes, Q6 (a)).
///
/// <para>
/// <b>The story, in plain language.</b> An employee whose weekly hours drop from 37 to 30 on Wednesday 16
/// September 2026 is now PAID for September: the host answers 200, exports exactly the overtime the owner's
/// per-working-day rule gives (2.67 at 50% and 4.53 at 100%, dated Sunday 20 September), and records a manifest
/// with the two parts. The one split that stays manual is an agreement change in the middle of a week (HK to
/// AC on Wednesday 18 March): the calculating routes AND the raw routes answer the week check's 422, the
/// same variant everywhere. And the two raw export routes (<c>/export</c>, <c>/export-period</c>), which map
/// every line under ONE profile, refuse any month with two parts (a change of hours included) under their own
/// named reason, <c>split-month-single-profile-route</c>, and write no export record.
/// </para>
///
/// <para>
/// <b>The factory (C-B9, R-58).</b> ONE <c>PayrollHostFactory(cs, new RuleRegistry().GetAll(), useRealRuleEngine:
/// true)</c> for the class. The shared stub answers payroll's delivery call with 404, which turns a COMMITTED
/// export into a 422 (<c>PayrollHostRecalcBlockedTests.cs:198-207</c>); the real-engine option answers it with 200
/// (<c>PayrollHostFactory.cs:61-82</c>), so a 200 here means "calculated and exported". SP1 needs the real engine;
/// it is harmless to IB3, RB3 and RB4, and under their mutations they then show 200 and an export record.
/// </para>
///
/// Docker-gated (Testcontainers Postgres + full init.sql): CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class SplitMonthHostTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string OrgId = "STY_S145_SPLITHOST";
    private const string Kind = "payroll-recalc-blocked";
    private const string MidWeekReason = "agreement-change-mid-week";
    private const string SingleProfileReason = "split-month-single-profile-route";
    private const string IsoDatePattern = @"\d{4}-\d{2}-\d{2}";
    private const decimal DayNorm = 7.4m;
    private const decimal DayNormAfter = 6.00m;

    /// <summary>The fixed sentence of the Q6 (a) body, verbatim from TASK-14522 contract 3.</summary>
    private const string SingleProfileSentence =
        "This period cannot be exported through this route: a change inside the period splits it into parts with " +
        "different profiles, and this route maps every line under a single profile. Use the calculating routes " +
        "(recalculate, or calculate-and-export) to pay the period.";

    private PostgreSqlContainer _container = null!;
    private DbConnectionFactory _db = null!;
    private PayrollHostFactory _factory = null!;
    private long _outboxSeq = 20000;

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

        // C-B9: the LIVE rule set for the planner, the REAL engine for every rule call (delivery answers 200).
        _factory = new PayrollHostFactory(cs, new RuleRegistry().GetAll(), useRealRuleEngine: true);
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // =====================================================================
    // SP1 - the positive, real-engine split month
    // =====================================================================

    /// <summary>
    /// SP1 (refinement :686-692). September 2026, HK, fraction 1.0 to 0.811 effective Wed 16 Sep, hours seeded in
    /// the projections (14-18 Sep at 8 h; every other weekday, 31 August included, at its per-day norm: 81.40 for
    /// the 11 weekdays 1-15 Sep and 66.00 for the 11 weekdays 16-30 Sep), August and September APPROVED.
    /// <c>POST /api/payroll/calculate-and-export</c> answers <b>200</b> and:
    /// <list type="bullet">
    /// <item>pays exactly OVERTIME_50 2.67 and OVERTIME_100 4.53, whose rule lines are dated Sunday 20 Sep, and the
    /// export record carries them under SLS_0210 2.67 and SLS_0220 4.53 (segment 2's codes; one agreement and
    /// position across the split, so the key does not differ) - the NORMAL_HOURS lines are there as usual (one
    /// per September weekday);</item>
    /// <item>records a manifest with two EMPLOYED segments and the boundary cause EmployeeProfileChange;</item>
    /// <item>reports the merged NORM_CHECK_37H row with NormHoursTotal 147.40 (81.40 + 66.00).</item>
    /// </list>
    /// (The flex figure +7.20 is WS1's, read through the recorder: the host result exposes no flex balance.)
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-7 (the evaluated-set filter disabled) lets the
    /// AlignedWindow rules reach the planner, which refuses the split month: the route answers 422 instead of 200.
    /// MB-3 and MB-8 at Unit level: MB-3 (the timeline covers only the first part) changes the lines and the norm
    /// total; MB-8 (the merge is given the per-segment rows again) fails the three timeline rules' merged rows
    /// (<c>SplitMonthPcsTests.TwoEmployedSegments_TimelineAwareRules_MergedRowIsTheTimelineRow</c>).
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_September_FractionChangeWed16Sep_PaysSplitWeek267And453_Dated20Sep_RealEngine()
    {
        const string employeeId = "EMP-S145-SP1";
        await SeedEmployeeAsync(employeeId, "HK", "OK26");
        await SupersedeProfileAsync(employeeId, fraction: 0.811m, effectiveFrom: Sep16);
        await SeedApprovalAsync(employeeId, "HK", "OK26", Aug01, Aug31);
        await SeedApprovalAsync(employeeId, "HK", "OK26", Sep01, Sep30);
        await SeedWeekdaysAsync(employeeId, "HK", Aug31, Sep30, d =>
            d >= Sep14 && d <= Sep18 ? 8.0m
            : d < Sep16 ? DayNorm
            : DayNormAfter);

        var response = await PostAsGlobalAdminAsync("/api/payroll/calculate-and-export",
            CalculateBody(employeeId, "HK", "OK26", Sep01, Sep30));
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)response.StatusCode}: {json}");
        var result = JsonSerializer.Deserialize<PeriodCalculationResult>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var overtime = result.RuleResults
            .SelectMany(r => r.LineItems)
            .Where(li => li.TimeType is "OVERTIME_50" or "OVERTIME_100" or "MERARBEJDE")
            .OrderBy(li => li.TimeType, StringComparer.Ordinal)
            .Select(li => (li.TimeType, li.Hours, li.Date))
            .ToList();
        var normalDates = result.RuleResults.SelectMany(r => r.LineItems)
            .Where(li => li.TimeType == "NORMAL_HOURS").Select(li => li.Date).OrderBy(d => d).ToList();
        var norm = result.RuleResults.Single(r => r.RuleId == "NORM_CHECK_37H");

        var exportRecords = await CountExportRecordsAsync(employeeId, 2026, 9);
        IReadOnlyList<PayrollExportLine> exported = exportRecords == 1
            ? await ReadExportedLinesAsync(employeeId, 2026, 9)
            : Array.Empty<PayrollExportLine>();
        var exportedOvertime = exported
            .Where(l => l.SourceTimeType is "OVERTIME_50" or "OVERTIME_100")
            .OrderBy(l => l.WageType, StringComparer.Ordinal)
            .Select(l => (l.WageType, l.Hours))
            .ToList();

        var manifest = await ReadManifestAsync(employeeId);

        Assert.Multiple(
            () => Assert.Equal(
                new[] { ("OVERTIME_100", 4.53m, Sep20), ("OVERTIME_50", 2.67m, Sep20) },
                overtime),
            () => Assert.Equal(1, exportRecords),
            () => Assert.Equal(new[] { ("SLS_0210", 2.67m), ("SLS_0220", 4.53m) }, exportedOvertime),
            () => Assert.Equal(Weekdays(Sep01, Sep30).ToList(), normalDates),
            () => Assert.Equal(147.40m, norm.NormHoursTotal),
            () => Assert.True(norm.Success, norm.ErrorMessage),
            // The manifest: two EMPLOYED segments (an EMPLOYED segment carries no employmentStatus member) and the
            // profile-change boundary.
            () => Assert.Equal(2, manifest.Segments.Count),
            () => Assert.All(manifest.Segments, s => Assert.False(s.TryGetProperty("employmentStatus", out _))),
            () => Assert.Contains("EmployeeProfileChange", manifest.BoundaryCauseSummary));
    }

    // =====================================================================
    // IB3 - the interior residual: HK -> AC on Wednesday 18 March
    // =====================================================================

    /// <summary>
    /// IB3 (refinement :698). An EXPORTED March 2026 (one export record) with February APPROVED and the agreement
    /// HK to AC effective WEDNESDAY 18 March (inside March and mid-week; two EMPLOYED segments).
    /// <c>POST /api/payroll/recalculate</c> as Global Admin answers <b>422</b> with the WEEK check's variant
    /// (<c>reason = agreement-change-mid-week</c>, no <c>ruleId</c>, no date, no employee id), not the planner's:
    /// after Stage B the planner no longer refuses this month, so the week check answers. The baseline
    /// <c>current_effective_lines</c> is byte-for-byte unchanged and no correction event or audit row is written.
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-4 (the week check ignores interior dates,
    /// <c>PeriodCalculationService.EnsureNoMidWeekAgreementChange</c>) lets the Wednesday change through: the month is recalculated
    /// and the route answers 200, tripping the status assertion. MB-7 (the filter disabled) restores the planner's
    /// refusal: the body then carries a <c>ruleId</c> and no <c>reason</c>, tripping <c>AssertWeekCheckRefusal</c>.
    /// </summary>
    [Fact]
    public async Task Recalculate_AgreementChangeWed18Mar_Returns422_AgreementChangeMidWeek_LinesUnchanged()
    {
        const string employeeId = "EMP-S145-IB3";
        var token = Guid.NewGuid();
        await SeedEmployeeAsync(employeeId, "HK", "OK24");
        await SeedApprovalAsync(employeeId, "HK", "OK24", Feb01, Feb28);
        await PersistMonthExportRecordAsync(employeeId, 2026, 3, "OK24");
        await SupersedeAgreementCodeAsync(employeeId, "AC", Mar18);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId, 2026, 3);

        var response = await PostAsGlobalAdminAsync("/api/payroll/recalculate", new
        {
            profile = Profile(employeeId, "HK", "OK24"),
            entries = Array.Empty<TimeEntry>(),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart = Mar01,
            periodEnd = Mar31,
            // S146 R-14: no opening balance in the body — it comes from the recorded ledger; any value is now a 400.
            reason = "S145 Stage B host pin - interior agreement change",
            idempotencyToken = token,
        });
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Mar01, Mar18, Mar31);
        Assert.Equal(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId, 2026, 3));
        Assert.Equal(0, await CountCorrectionEventsAsync(employeeId));
    }

    // =====================================================================
    // RB3 / RB4 - the raw export routes (Q6 (a))
    // =====================================================================

    /// <summary>
    /// RB3 (refinement :700, Q6 (a)). Raw <c>POST /api/payroll/export</c> as Global Admin, with a caller-calculated
    /// April-2026 result (NORMAL_HOURS lines on 2 and 20 April) for an HK employee whose part-time fraction changes
    /// 1.0 to 0.8 on Thursday 16 April (a change of HOURS only: no agreement change, so the week check passes and
    /// only the single-profile rule can refuse). The route maps every line under ONE profile and cannot export a
    /// month with two parts correctly, so it answers <b>422</b> with the third variant: <c>kind
    /// payroll-recalc-blocked</c>, <c>reason split-month-single-profile-route</c>, <c>employedSegmentCount 2</c>,
    /// causes <c>["EmployeeProfileChange"]</c>, the fixed sentence, and NO <c>ruleId</c>, date or employee id. No
    /// <c>payroll_export_records</c> row is written.
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-5 (the Q6 (a) check removed) lets the route map and export:
    /// the delivery answers 200 (C-B9), the route answers <b>200</b> and an April record exists, tripping the 422
    /// and zero-row assertions. MB-7 (the filter disabled) restores the planner's refusal: a body with a
    /// <c>ruleId</c> and no <c>reason</c> trips the variant assertions.
    /// </summary>
    [Fact]
    public async Task Export_FractionChangeMidMonth_Returns422_SplitMonthSingleProfileRoute_NoExportRecord()
    {
        const string employeeId = "EMP-S145-RB3";
        await SeedEmployeeAsync(employeeId, "HK", "OK26");
        await SupersedeProfileAsync(employeeId, fraction: 0.8m, effectiveFrom: Apr16);

        var body = new PayrollExportRequest
        {
            CalculationResult = RawResult(employeeId, Apr02, Apr20),
            Profile = Profile(employeeId, "HK", "OK26"),
        };

        var response = await PostAsGlobalAdminAsync("/api/payroll/export", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertSingleProfileRefusal(json, employeeId, Apr01, Apr16, Apr30);
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, 2026, 4));
    }

    /// <summary>
    /// RB4 (TASK-14522 contract 3, R-74: the raw-route ORDER, planner 422 then week-check 422 then Q6 (a) 422).
    /// Raw <c>POST /api/payroll/export</c> for an HK employee whose agreement becomes AC on WEDNESDAY 18 March 2026
    /// (two EMPLOYED segments: both a mid-week agreement change AND a split month) answers the WEEK variant
    /// (<c>reason agreement-change-mid-week</c>), not the single-profile reason: the raw guard must answer an
    /// interior Wednesday agreement change the way the calculating routes do ("one predicate, three consumers").
    /// No export record.
    ///
    /// Red conditions (Docker-gated, CI-verified): MB-4 (the week check ignores interior dates) makes the Q6 (a)
    /// check answer instead: the body's <c>reason</c> is <c>split-month-single-profile-route</c>, tripping
    /// <c>AssertWeekCheckRefusal</c>. MB-7 (the filter disabled) restores the planner's refusal (a <c>ruleId</c>, no
    /// <c>reason</c>), tripping the same assertion.
    /// </summary>
    [Fact]
    public async Task Export_AgreementChangeWed18Mar_Returns422_AgreementChangeMidWeek_NotTheSingleProfileReason()
    {
        const string employeeId = "EMP-S145-RB4";
        await SeedEmployeeAsync(employeeId, "HK", "OK24");
        await SupersedeAgreementCodeAsync(employeeId, "AC", Mar18);

        var body = new PayrollExportRequest
        {
            CalculationResult = RawResult(employeeId, Mar02, Mar20),
            Profile = Profile(employeeId, "HK", "OK24"),
        };

        var response = await PostAsGlobalAdminAsync("/api/payroll/export", body);
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected 422, got {(int)response.StatusCode}: {json}");
        AssertWeekCheckRefusal(json, employeeId, Mar01, Mar18, Mar31);
        Assert.Equal(0, await CountExportRecordsAsync(employeeId, 2026, 3));
    }

    // =====================================================================
    // Assertions
    // =====================================================================

    /// <summary>The third 422 variant: kind payroll-recalc-blocked, reason split-month-single-profile-route, two
    /// EMPLOYED segments, cause EmployeeProfileChange, the fixed sentence, no ruleId, no date in any rendering, no
    /// employee id.</summary>
    private static void AssertSingleProfileRefusal(string json, string employeeId, params DateOnly[] datesThatMustNotLeak)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(Kind, JsonAssert.RequiredMember(root, "kind").GetString());
        Assert.False(JsonAssert.RequiredMember(root, "success").GetBoolean());
        Assert.Equal(SingleProfileReason, JsonAssert.RequiredMember(root, "reason").GetString());
        Assert.Equal(2, JsonAssert.RequiredMember(root, "employedSegmentCount").GetInt32());
        Assert.Equal(new[] { "EmployeeProfileChange" },
            JsonAssert.RequiredMember(root, "interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(SingleProfileSentence, JsonAssert.RequiredMember(root, "error").GetString());
        Assert.True(
            !root.TryGetProperty("ruleId", out var ruleId) || ruleId.ValueKind == JsonValueKind.Null,
            "the single-profile-route variant carries no ruleId (it is not a rule's refusal)");

        AssertNoDatesOrEmployee(json, employeeId, datesThatMustNotLeak);
    }

    /// <summary>The week check's 422 variant: kind payroll-recalc-blocked, reason agreement-change-mid-week, cause
    /// AgreementCodeChange, no ruleId, no date in any rendering, no employee id.</summary>
    private static void AssertWeekCheckRefusal(string json, string employeeId, params DateOnly[] datesThatMustNotLeak)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(Kind, JsonAssert.RequiredMember(root, "kind").GetString());
        Assert.Equal(MidWeekReason, JsonAssert.RequiredMember(root, "reason").GetString());
        Assert.False(JsonAssert.RequiredMember(root, "success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(JsonAssert.RequiredMember(root, "error").GetString()));
        Assert.Equal(new[] { "AgreementCodeChange" },
            JsonAssert.RequiredMember(root, "interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.True(
            !root.TryGetProperty("ruleId", out var ruleId) || ruleId.ValueKind == JsonValueKind.Null,
            "the week check's variant carries no ruleId (it is not a rule's refusal)");

        AssertNoDatesOrEmployee(json, employeeId, datesThatMustNotLeak);
    }

    private static void AssertNoDatesOrEmployee(string json, string employeeId, DateOnly[] dates)
    {
        Assert.DoesNotMatch(IsoDatePattern, json);
        foreach (var date in dates)
        {
            Assert.DoesNotContain(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), json);
            Assert.DoesNotContain(date.ToString(), json);
            Assert.DoesNotContain(date.ToString(CultureInfo.InvariantCulture), json);
        }
        Assert.DoesNotContain(employeeId, json);
    }

    // =====================================================================
    // Dates
    // =====================================================================

    private static readonly DateOnly Feb01 = new(2026, 2, 1);
    private static readonly DateOnly Feb28 = new(2026, 2, 28);
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar02 = new(2026, 3, 2);
    private static readonly DateOnly Mar18 = new(2026, 3, 18);
    private static readonly DateOnly Mar20 = new(2026, 3, 20);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr02 = new(2026, 4, 2);
    private static readonly DateOnly Apr16 = new(2026, 4, 16);
    private static readonly DateOnly Apr20 = new(2026, 4, 20);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);
    private static readonly DateOnly Aug01 = new(2026, 8, 1);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep14 = new(2026, 9, 14);
    private static readonly DateOnly Sep16 = new(2026, 9, 16);
    private static readonly DateOnly Sep18 = new(2026, 9, 18);
    private static readonly DateOnly Sep20 = new(2026, 9, 20);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);

    private static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            yield return d;
        }
    }

    // =====================================================================
    // HTTP
    // =====================================================================

    private async Task<HttpResponseMessage> PostAsGlobalAdminAsync(string path, object body)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, body.GetType()),
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
        // GLOBAL scope: the resource guard admits a GLOBAL-scoped actor for any employee. The host forwards this
        // token to the Rule Engine, which validates it against the same dev key.
        return tokenService.GenerateToken(
            employeeId: "ROOT-S145",
            name: "ROOT-S145",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static EmploymentProfile Profile(string employeeId, string agreementCode, string okVersion) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    /// <summary>A calculating-route body with NO hours (R14): empty lists are accepted, non-empty ones answer 400.</summary>
    private static object CalculateBody(
        string employeeId, string agreementCode, string okVersion, DateOnly periodStart, DateOnly periodEnd) => new
    {
        profile = Profile(employeeId, agreementCode, okVersion),
        entries = Array.Empty<TimeEntry>(),
        absences = Array.Empty<AbsenceEntry>(),
        periodStart,
        periodEnd,
        // S146 R-14: no opening balance in the body — it comes from the recorded ledger; any value is now a 400.
    };

    /// <summary>A caller-calculated result: one NORMAL_HOURS line (7.4 h) on each given date - the shape the raw
    /// routes receive (lines already computed; the route only maps them).</summary>
    private static CalculationResult RawResult(string employeeId, params DateOnly[] dates) => new()
    {
        RuleId = "NORM_CHECK_37H",
        EmployeeId = employeeId,
        Success = true,
        LineItems = dates
            .Select(d => new CalculationLineItem { TimeType = "NORMAL_HOURS", Hours = 7.4m, Rate = 0m, Date = d })
            .ToList(),
    };

    // =====================================================================
    // Seeding
    // =====================================================================

    /// <summary>users + employee_profiles + user_agreement_codes (history-covering '0001-01-01' rows) + the
    /// organization. Employment dates NULL: employed throughout.</summary>
    private async Task SeedEmployeeAsync(string employeeId, string agreementCode, string okVersion)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, agreementCode, okVersion, partTimeFraction: 1.0m);
    }

    /// <summary>Closes the live profile row at <paramref name="effectiveFrom"/> (end-exclusive) and inserts the
    /// successor with the new fraction.</summary>
    private async Task SupersedeProfileAsync(string employeeId, decimal fraction, DateOnly effectiveFrom)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var close = new NpgsqlCommand(
            "UPDATE employee_profiles SET effective_to = @from WHERE employee_id = @id AND effective_to IS NULL", conn, tx))
        {
            close.Parameters.AddWithValue("id", employeeId);
            close.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @id, @fraction, NULL, @from, NULL, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @id))
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("fraction", fraction);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }

    /// <summary>Closes the live agreement-code row at <paramref name="effectiveFrom"/> (end-exclusive) and
    /// inserts the successor.</summary>
    private async Task SupersedeAgreementCodeAsync(string employeeId, string newAgreementCode, DateOnly effectiveFrom)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var close = new NpgsqlCommand(
            "UPDATE user_agreement_codes SET effective_to = @from WHERE user_id = @id AND effective_to IS NULL", conn, tx))
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

    /// <summary>An APPROVED monthly approval row for any month: the month's own approval (the
    /// <c>/calculate-and-export</c> guard) or "the previous month is APPROVED" (R26's host-fact rule).</summary>
    private async Task SeedApprovalAsync(
        string employeeId, string agreementCode, string okVersion, DateOnly periodStart, DateOnly periodEnd)
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
                NOW(), @emp, 'ROOT-S145', NOW(), @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        cmd.Parameters.AddWithValue("agreement", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>One RAW time-entry projection row per WEEKDAY in [from, to] (a current TIME_POST row).</summary>
    private async Task SeedWeekdaysAsync(
        string employeeId, string agreementCode, DateOnly from, DateOnly to, Func<DateOnly, decimal> hoursFor)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        foreach (var date in Weekdays(from, to))
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, @hours, @agreement, 'OK26', FALSE, NOW(), @outbox)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", date);
            cmd.Parameters.AddWithValue("hours", hoursFor(date));
            cmd.Parameters.AddWithValue("agreement", agreementCode);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
    }

    /// <summary>The FIRST-export record for a calendar month (<c>original_lines == current_effective_lines</c>),
    /// written with the production manifest serializer: one NORMAL_HOURS line, null period id.</summary>
    private async Task PersistMonthExportRecordAsync(string employeeId, int year, int month, string okVersion)
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
                OkVersion = okVersion,
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

    // =====================================================================
    // Reads
    // =====================================================================

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

    private async Task<string> ReadCurrentEffectiveLinesAsync(string employeeId, int year, int month)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT current_effective_lines::text FROM payroll_export_records WHERE employee_id = @e AND year = @y AND month = @m", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "no payroll_export_records row");
        return reader.GetString(0);
    }

    /// <summary>The export record's current effective lines, through the production manifest deserializer.</summary>
    private async Task<IReadOnlyList<PayrollExportLine>> ReadExportedLinesAsync(string employeeId, int year, int month) =>
        PayrollExportManifest.Deserialize(await ReadCurrentEffectiveLinesAsync(employeeId, year, month));

    private async Task<int> CountCorrectionEventsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested' AND stream_id LIKE @s", conn);
        cmd.Parameters.AddWithValue("s", $"retro-correction-{employeeId}-%");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private sealed record PersistedManifest(IReadOnlyList<JsonElement> Segments, IReadOnlyList<string> BoundaryCauseSummary);

    /// <summary>The employee's ONE persisted manifest (the projection row).</summary>
    private async Task<PersistedManifest> ReadManifestAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT segments_jsonb::text, boundary_cause_summary FROM segment_manifests WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no segment_manifests row for {employeeId}");

        using var doc = JsonDocument.Parse(reader.GetString(0));
        var segments = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        var summary = ((string[])reader.GetValue(1)).ToList();
        Assert.False(await reader.ReadAsync(), "more than one manifest row for the employee");
        return new PersistedManifest(segments, summary);
    }
}
