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
    /// <c>AgreementCodeEffectiveDates</c> hydration (passes null), OR the host stops injecting
    /// <c>UserAgreementCodeRepository</c> into the service: the month plans as ONE segment, the
    /// stubbed rule engine answers, the correction commits, and the endpoint returns <b>200</b> —
    /// <c>Assert.Equal(422, status)</c> trips (and the baseline changes).
    /// (2) the <c>/recalculate</c> <c>PlannerInvariantViolation</c> catch is removed → <b>500</b>,
    /// same assertion trips.
    /// (3) mutation M-13 (<c>error = ex.Message</c>) → the body carries the period dates and the
    /// employee id; the redaction assertions trip.
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
    /// <c>CalculateWithOutcomeAsync</c> is removed → the refusal escapes as <b>500</b>;
    /// <c>Assert.Equal(422, status)</c> trips.
    /// (2) mutation M-14 (hydration skipped / repository not injected) → one segment, the month
    /// calculates and exports: <b>200</b>, plus an export record and a manifest — the status and
    /// both row-count assertions trip.
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
