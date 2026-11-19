using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Organisation;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14822-T (ADR-040 D4, ADR-043 D12, ADR-016 D10; AC-C13a split half, AC-C13b, AC-C13t) — HOST-LEVEL
/// pins that payroll knows which institution each day belongs to: a transfer cuts the month, and a month already
/// exported is not touched by a transfer recorded later.
///
/// <para>
/// <b>The story, in plain language.</b> Mette (HK) works at Styrelse STY01 and moves to STY02 on Monday 15 June 2026.
/// Her first half of June belongs to STY01 and her second half to STY02, and each institution may have its own
/// local agreement profile, so payroll must calculate June in two parts, 1-14 and 15-30 June, and say why it cut
/// there (the existing "employee record changed" reason, <c>EmployeeProfileChange</c>). Two cuts nobody else would
/// see are pinned too: STY02's own local profile starting a week after the transfer (a third part), and a transfer
/// on the last day of June, which splits July's opening-week context days (Monday 29 and Tuesday 30 June) into two
/// frozen pieces. The other way round, a transfer recorded back to 15 June AFTER June was exported changes nothing
/// that was paid: the export record, its lines and its content hash stay exactly as they were, and nothing
/// recalculates by itself (ADR-013).
/// </para>
///
/// <para>
/// <b>Harness.</b> The real Payroll host (<see cref="PayrollHostFactory"/>, the
/// <c>PayrollHostRecalcBlockedTests</c> shape) over one Testcontainers Postgres with the full schema; the person is
/// seeded through <see cref="RegressionSeed"/>, so the first <c>user_organisations</c> row starts at the SQL literal
/// <c>0001-01-01</c> (a parameter bound to <see cref="DateOnly.MinValue"/> would store <c>-infinity</c>). Three
/// facts write their transfer by SQL (<see cref="RecordTransferAsync"/>; what it reproduces and what it does not is
/// on the helper): there every assertion is about what payroll reads, which is the same either way. The
/// exported-month fact records its transfer through the Backend user-edit route on a Backend host over the same
/// container, because only the route runs product code between that fact's two readings. Facts run one at
/// a time (<c>tests/StatsTid.Tests.Regression/xunit.runner.json</c>); each uses its own employee, and the one fact that
/// seeds a local profile removes it again. Recorded hours (7.4 h on every weekday) are seeded so every month has
/// lines to export and writes its record.
/// </para>
///
/// <para>
/// Docker-gated (Testcontainers Postgres + full <c>init.sql</c>): CI-pending; argued statically, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TransferPayrollBoundaryTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string FromOrg = "STY01";
    private const string ToOrg = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK26";

    // 1, 15 and 22 June 2026 are Mondays, 30 June a Tuesday, 1 July a Wednesday.
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun14 = new(2026, 6, 14);
    private static readonly DateOnly Jun15 = new(2026, 6, 15);
    private static readonly DateOnly Jun21 = new(2026, 6, 21);
    private static readonly DateOnly Jun22 = new(2026, 6, 22);
    private static readonly DateOnly Jun29 = new(2026, 6, 29);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);
    private static readonly DateOnly Jul01 = new(2026, 7, 1);
    private static readonly DateOnly Jul31 = new(2026, 7, 31);

    private PostgreSqlContainer _container = null!;
    private DbConnectionFactory _db = null!;
    private PayrollHostFactory _factory = null!;
    private long _outboxSeq = 5000;
    private string _cs = null!;
    private StatsTidWebApplicationFactory? _backendFactory;
    private WebApplicationFactory<Program>? _backendHost;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        var cs = _cs = _container.GetConnectionString();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(cs);
        _db = new DbConnectionFactory(cs);
        await TestFixtures.SeedWageTypeMappingsAsync(_db);
        _factory = new PayrollHostFactory(cs, new RuleRegistry().GetAll());
    }

    /// <summary>A Backend host over this container, its Copenhagen "today" pinned to <paramref name="today"/> and its leaver
    /// job off (no seeded person is switched off behind a fact's back). Booted by the one fact that records its transfer
    /// through the route, before that fact seeds anyone (PAT-008).</summary>
    private WebApplicationFactory<Program> BootBackendHost(DateOnly today)
    {
        _backendFactory = new StatsTidWebApplicationFactory(_cs, withoutLeaverJob: true);
        _backendHost = _backendFactory.WithFixedToday(today);
        _ = _backendHost.CreateClient();
        return _backendHost;
    }

    public async Task DisposeAsync()
    {
        _backendHost?.Dispose();
        _backendFactory?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C13a (split half): a transfer on Monday 15 June 2026 splits June
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C13a's split half. An HK person in STY01 moves to STY02 on Monday 15 June 2026; June is
    /// APPROVED. <c>POST /api/payroll/calculate-and-export</c> as Global Admin for June 2026 → 200, exactly ONE
    /// <c>segment_manifests</c> row, with <b>two</b> segments, 1-14 and 15-30 June, both EMPLOYED, and
    /// <c>boundary_cause_summary = {EmployeeProfileChange}</c>. Both segments carry that cause (FAIL-007: with two
    /// segments each carries the cause of the boundary between them). The norm half of AC-C13a is TASK-14828-T's.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). On the code before TASK-14822 no transfer date reaches the
    /// planner, so June is ONE segment (1-30 June) and <c>Assert.Equal(2, manifest.Segments.Count)</c> fails with 1.
    /// With the reader registered but a mutation that ignores its dates the same assertion trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TransferMonday15June2026_SplitsJune()
    {
        const string employeeId = "EMP-S148-TRANSFER-SPLIT";
        await SeedTransferredEmployeeAsync(employeeId, Jun15);
        await SeedWeekdayHoursAsync(employeeId, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, Jun01, Jun30);

        var response = await CalculateAndExportAsync(employeeId, Jun01, Jun30);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)response.StatusCode}: {json}");

        var manifest = await LoadSingleManifestAsync(employeeId);
        Assert.Equal(2, manifest.Segments.Count);
        Assert.Equal((Jun01, Jun14), (manifest.Segments[0].Start, manifest.Segments[0].End));
        Assert.Equal((Jun15, Jun30), (manifest.Segments[1].Start, manifest.Segments[1].End));
        Assert.All(manifest.Segments, s => Assert.True(s.Employed));
        Assert.Equal(new[] { "EmployeeProfileChange" }, manifest.BoundaryCauseSummary);
        Assert.Equal(new[] { "EmployeeProfileChange", "EmployeeProfileChange" }, manifest.Segments.Select(s => s.Cause));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C13b: a backdated transfer changes no exported month
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind R (CI).</b> AC-C13b. An HK person in STY01 throughout; June 2026 is APPROVED and exported
    /// (<c>calculate-and-export</c> → 200, one export record with lines, one manifest). The transfer to STY02
    /// effective Monday 15 June 2026 is then recorded THROUGH THE PRODUCT: <c>PUT /api/admin/users/{id}</c> on a Backend
    /// host over the same container (clock fixed at 10 July 2026, so the date is a past one). Payroll's
    /// <c>calculate-and-export</c> for June is then called AGAIN and must answer either 200 (the idempotent no-op) or
    /// the 409 <c>payroll-export-conflict</c> ("use a correction"); today it answers 409, because the export lines carry
    /// the calculation's own manifest id and so the content hash of a repeat never equals the first (a repeat WITHOUT a
    /// transfer answers 409 as well). Any other answer (422, 500, 503) fails the fact. Asserts: June's
    /// <c>payroll_export_records</c> row keeps its <c>content_hash</c>, <c>original_lines</c>,
    /// <c>current_effective_lines</c> and <c>exported_at</c>, and there is still ONE export row for the person.
    ///
    /// <para>
    /// <b>Product code that runs between the two readings:</b> the Backend user-edit route (it writes the dated
    /// organisation rows) and payroll's second calculation, whose planner now reads the transfer date (the recalculation
    /// writes its own manifest, ADR-016 D10, so the manifest count rises from 1 to 2 and the new manifest holds TWO
    /// segments, 1-14 and 15-30 June). The discriminating assertion is that the June export record is unchanged AFTER a
    /// calculation that DID see the transfer (the two-segment manifest is the proof that it did): <c>content_hash</c>
    /// equality with the lines and the timestamp, and a single export row.
    /// </para>
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). R: the record holds on the code before
    /// TASK-14822 as well (no transfer date reaches the planner, the new manifest has ONE segment, so the two-segment
    /// assertion fails there, which is the proof that the second calculation is not a repeat of the first). The record
    /// assertions trip if recording a transfer or recalculating after it ever rewrote, re-exported or changed an
    /// exported month. The first captured row is asserted non-empty so the fact cannot pass on a record that was never written.
    /// </para>
    /// </summary>
    [Fact]
    public async Task BackdatedTransfer_ExportedMonthUnchanged()
    {
        const string employeeId = "EMP-S148-TRANSFER-EXPORTED";
        // Boot order (PAT-008): the Backend host boots before any fixture is seeded.
        var backend = BootBackendHost(new DateOnly(2026, 7, 10));
        await SeedEmployeeAsync(employeeId, FromOrg);
        await SeedWeekdayHoursAsync(employeeId, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, Jun01, Jun30);

        var response = await CalculateAndExportAsync(employeeId, Jun01, Jun30);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)response.StatusCode}: {json}");

        var before = await LoadExportRecordAsync(employeeId);
        Assert.False(string.IsNullOrEmpty(before.ContentHash));
        Assert.NotEqual("[]", before.OriginalLines);
        var manifestsBefore = await CountManifestsAsync(employeeId);
        Assert.Equal(1, manifestsBefore);

        // The transfer, recorded by the product's own route.
        var put = await TransferWriteTests.PutUserAsync(
            TransferWriteTests.GlobalAdminClient(backend), _cs, employeeId,
            new { primaryOrgId = ToOrg, effectiveFrom = "2026-06-15" });
        Assert.True(put.StatusCode == HttpStatusCode.OK,
            $"expected 200 from the user-edit route, got {(int)put.StatusCode}: {await put.Content.ReadAsStringAsync()}");

        // Payroll calculates June again, now knowing the transfer.
        var again = await CalculateAndExportAsync(employeeId, Jun01, Jun30);
        var againJson = await again.Content.ReadAsStringAsync();
        Assert.True(
            again.StatusCode == HttpStatusCode.OK
            || (again.StatusCode == HttpStatusCode.Conflict && againJson.Contains("\"kind\":\"payroll-export-conflict\"", StringComparison.Ordinal)),
            $"expected 200 or the 409 payroll-export-conflict, got {(int)again.StatusCode}: {againJson}");

        // The second calculation saw the transfer (its own manifest has two segments)...
        Assert.Equal(manifestsBefore + 1, await CountManifestsAsync(employeeId));
        Assert.Equal(2, await LatestManifestSegmentCountAsync(employeeId));

        // ...and the exported month is exactly as it was.
        var after = await LoadExportRecordAsync(employeeId);
        Assert.Equal(before.ContentHash, after.ContentHash);
        Assert.Equal(before.OriginalLines, after.OriginalLines);
        Assert.Equal(before.CurrentEffectiveLines, after.CurrentEffectiveLines);
        Assert.Equal(before.ExportedAt, after.ExportedAt);
        Assert.Equal(1, await CountExportRecordsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C13t: the two cuts nothing else sees
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C13t (1). The split fact's seed (STY01 to STY02 on Monday 15 June 2026) plus a local
    /// profile for (STY02, HK, OK26) effective from Monday 22 June 2026 (SQL into <c>local_agreement_profiles</c>; no
    /// earlier row exists for that key). <c>calculate-and-export</c> June → 200: THREE segments, 1-14, 15-21 and
    /// 22-30 June; per FAIL-007 (with three segments the first and the middle carry the cause of the boundary that
    /// ENDS them, the last the cause that started it) segment 0 carries <c>EmployeeProfileChange</c>, segments 1 and 2
    /// <c>LocalProfileActivation</c>. The profile row is removed afterwards, so no other fact sees it.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before TASK-14822 the local-profile lookup reads one key
    /// for the whole month, on 1 June, so it asks STY01's history, finds no activation, and misses STY02's: the
    /// month plans as ONE segment (the transfer date is no cut either), so <c>Assert.Equal(3, …Count)</c> fails with 1.
    /// With the transfer cut but the lookup left on STY01 the plan has two segments and the same assertion fails with
    /// 2.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TransferMonday15June2026_NewOrganisationsProfileActivation_SplitsAtIt()
    {
        const string employeeId = "EMP-S148-TRANSFER-ACTIVATION";
        await SeedTransferredEmployeeAsync(employeeId, Jun15);
        await SeedWeekdayHoursAsync(employeeId, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, Jun01, Jun30);
        await SeedLocalProfileAsync(ToOrg, Jun22);
        try
        {
            var response = await CalculateAndExportAsync(employeeId, Jun01, Jun30);
            var json = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)response.StatusCode}: {json}");

            var manifest = await LoadSingleManifestAsync(employeeId);
            Assert.Equal(3, manifest.Segments.Count);
            Assert.Equal((Jun01, Jun14), (manifest.Segments[0].Start, manifest.Segments[0].End));
            Assert.Equal((Jun15, Jun21), (manifest.Segments[1].Start, manifest.Segments[1].End));
            Assert.Equal((Jun22, Jun30), (manifest.Segments[2].Start, manifest.Segments[2].End));
            Assert.Equal(
                new[] { "EmployeeProfileChange", "LocalProfileActivation", "LocalProfileActivation" },
                manifest.Segments.Select(s => s.Cause));
        }
        finally
        {
            await DeleteLocalProfilesAsync(ToOrg);
        }
    }

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C13t (2). A transfer on Tuesday 30 June 2026 (STY01 to 30 June, STY02 from 30 June).
    /// July 2026 opens on Wednesday 1 July, so its opening week's context days are Monday 29 and Tuesday 30 June. June
    /// 2026 is APPROVED (the previous-month check) and so is July; no export record exists, so the flex ledger is empty
    /// and July opens at 0. <c>calculate-and-export</c> July → 200. The manifest's frozen pieces begin with TWO context
    /// pieces, [2026-06-29, 2026-06-29] and [2026-06-30, 2026-06-30], then July's own piece (from 1 July).
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). Before TASK-14822 the context days are ONE piece,
    /// [2026-06-29, 2026-06-30]: the frozen pieces number two (one context piece, then July's own), not three, so the count guard
    /// (<c>pieces.Count >= 3</c>) fails first; were the count met, the first piece's end date (30 June, not 29 June) is the next to fail.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TransferTue30June2026_InJulysOpeningWeek_CutsTheContextPiece()
    {
        const string employeeId = "EMP-S148-TRANSFER-CONTEXT";
        await SeedTransferredEmployeeAsync(employeeId, Jun30);
        await SeedWeekdayHoursAsync(employeeId, Jun29, Jul31);
        await SeedApprovedMonthAsync(employeeId, Jun01, Jun30);
        await SeedApprovedMonthAsync(employeeId, Jul01, Jul31);

        var response = await CalculateAndExportAsync(employeeId, Jul01, Jul31);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)response.StatusCode}: {json}");

        var pieces = (await LoadSingleManifestAsync(employeeId)).Pieces;
        Assert.True(pieces.Count >= 3, $"expected at least 3 frozen pieces, got {pieces.Count}");
        Assert.Equal((Jun29, Jun29), pieces[0]);
        Assert.Equal((Jun30, Jun30), pieces[1]);
        Assert.Equal(Jul01, pieces[2].From);
    }

    // ═════════════════════════════════════════════════════════════════════
    // HTTP
    // ═════════════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> CalculateAndExportAsync(string employeeId, DateOnly start, DateOnly end)
    {
        var body = new
        {
            profile = new EmploymentProfile
            {
                EmployeeId = employeeId,
                AgreementCode = AgreementCode,
                OkVersion = OkVersion,
                EmploymentCategory = "Standard",
                PartTimeFraction = 1.0m,
                OrgId = ToOrg,
            },
            periodStart = start,
            periodEnd = end,
        };

        // The delivering host: after a committed export, payroll posts to mock payroll; the shared host's stub
        // answers that post with 404, which the route would report as a 422 although the month was paid.
        var client = DeliveringFactory().CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return await client.SendAsync(request);
    }

    private WebApplicationFactory<RetroactiveCorrectionService> DeliveringFactory() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new DeliveringClientFactory());
        }));

    private sealed class DeliveringClientFactory : IHttpClientFactory
    {
        // Exactly POST /api/payroll/receive; every other outbound call goes to the shared Rule Engine stub.
        private readonly HttpMessageHandler _handler = new TestFixtures.StubHandler(request =>
            request.Method == HttpMethod.Post
            && string.Equals(request.RequestUri?.AbsolutePath, "/api/payroll/receive", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : TestFixtures.DefaultRuleEngineHandler(request));

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
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
        return tokenService.GenerateToken(
            employeeId: "ROOT-S148",
            name: "ROOT-S148",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    // ═════════════════════════════════════════════════════════════════════
    // Seeding
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>users + employee_profiles + user_agreement_codes + the first user_organisations row from the SQL
    /// literal <c>0001-01-01</c> (RegressionSeed), in <paramref name="orgId"/>; employed the whole time.</summary>
    private async Task SeedEmployeeAsync(string employeeId, string orgId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await RegressionSeed.SeedEmployeeAsync(conn, employeeId, orgId, AgreementCode, OkVersion, partTimeFraction: 1.0m);
    }

    /// <summary>A person seeded in STY01 who moved to STY02 on <paramref name="transferDate"/>.</summary>
    private async Task SeedTransferredEmployeeAsync(string employeeId, DateOnly transferDate)
    {
        await SeedEmployeeAsync(employeeId, FromOrg);
        await RecordTransferAsync(employeeId, transferDate);
    }

    /// <summary>
    /// A transfer to STY02 effective <paramref name="effectiveFrom"/>, written by SQL, for the facts whose subject is
    /// payroll's reading of the dates. It reproduces the route's row shape: the open row's <c>effective_to</c> becomes
    /// the date (end exclusive; the first row keeps its <c>0001-01-01</c> start), the successor row starts on the date
    /// with a NULL <c>effective_to</c>, <c>users.primary_org_id</c> follows, and the versions are the route's (the closed
    /// row keeps its version; the successor is the predecessor's version + 1). It does NOT write what else the route
    /// writes: the SUPERSEDED <c>user_organisations_audit</c> row, the <c>UserOrganisationTransferred</c> event and its
    /// projection, or the reporting-line and vikar fan-out. A fact that needs those goes through the route (as
    /// <see cref="BackdatedTransfer_ExportedMonthUnchanged"/> does).
    /// </summary>
    private async Task RecordTransferAsync(string employeeId, DateOnly effectiveFrom)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var close = new NpgsqlCommand(
            "UPDATE user_organisations SET effective_to = @d WHERE user_id = @u AND effective_to IS NULL",
            conn, tx))
        {
            close.Parameters.AddWithValue("d", effectiveFrom);
            close.Parameters.AddWithValue("u", employeeId);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }
        await using (var open = new NpgsqlCommand(
            """
            INSERT INTO user_organisations (assignment_id, user_id, org_id, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, @o, @d, NULL, 2)
            """, conn, tx))
        {
            open.Parameters.AddWithValue("u", employeeId);
            open.Parameters.AddWithValue("o", ToOrg);
            open.Parameters.AddWithValue("d", effectiveFrom);
            Assert.Equal(1, await open.ExecuteNonQueryAsync());
        }
        await using (var primary = new NpgsqlCommand(
            "UPDATE users SET primary_org_id = @o WHERE user_id = @u", conn, tx))
        {
            primary.Parameters.AddWithValue("o", ToOrg);
            primary.Parameters.AddWithValue("u", employeeId);
            Assert.Equal(1, await primary.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }

    /// <summary>7.4 h on every weekday in the range, as raw <c>time_entries_projection</c> rows (the shape
    /// <c>PayrollHostRecalcBlockedTests</c> seeds): the stub engine answers one NORMAL_HOURS line per entry, and
    /// <c>/calculate-and-export</c> writes an export record when the calculation produced lines.</summary>
    private async Task SeedWeekdayHoursAsync(string employeeId, DateOnly from, DateOnly to)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        for (var day = from; day <= to; day = day.AddDays(1))
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
            cmd.Parameters.AddWithValue("agreement", AgreementCode);
            cmd.Parameters.AddWithValue("ok", OkVersion);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
    }

    /// <summary>An APPROVED monthly approval period (the /calculate-and-export guard, and the previous-month check).</summary>
    private async Task SeedApprovedMonthAsync(string employeeId, DateOnly start, DateOnly end)
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
                NOW(), @emp, 'ROOT-S148', NOW(), @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", FromOrg);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        cmd.Parameters.AddWithValue("agreement", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>An open-ended local profile of (org, HK, OK26) effective from the date (no earlier row exists for the
    /// key in this container, so nothing needs closing).</summary>
    private async Task SeedLocalProfileAsync(string orgId, DateOnly effectiveFrom)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by)
            VALUES (
                gen_random_uuid(), @org, @ac, @ok,
                @from, NULL,
                36, NULL, NULL, NULL,
                'admin1')
            """, conn);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task DeleteLocalProfilesAsync(string orgId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM local_agreement_profiles WHERE org_id = @org", conn);
        cmd.Parameters.AddWithValue("org", orgId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Reads
    // ═════════════════════════════════════════════════════════════════════

    private sealed record SegmentRow(DateOnly Start, DateOnly End, string Cause, bool Employed);

    private sealed record ManifestRow(
        IReadOnlyList<SegmentRow> Segments,
        IReadOnlyList<string> BoundaryCauseSummary,
        IReadOnlyList<(DateOnly From, DateOnly To)> Pieces);

    /// <summary>The employee's ONE <c>segment_manifests</c> row (asserts there is exactly one): its segments in order,
    /// its cause summary and its frozen pieces (<c>plan_inputs_jsonb.pieces</c>, context pieces first).</summary>
    private async Task<ManifestRow> LoadSingleManifestAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT segments_jsonb::text, boundary_cause_summary, plan_inputs_jsonb::text
            FROM segment_manifests
            WHERE employee_id = @e
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no segment_manifests row for {employeeId}");

        using var segmentsDoc = JsonDocument.Parse(reader.GetString(0));
        var segments = segmentsDoc.RootElement.EnumerateArray()
            .Select(s => new SegmentRow(
                DateOnly.Parse(s.GetProperty("startDate").GetString()!, CultureInfo.InvariantCulture),
                DateOnly.Parse(s.GetProperty("endDate").GetString()!, CultureInfo.InvariantCulture),
                s.GetProperty("boundaryCause").GetString()!,
                !s.TryGetProperty("employmentStatus", out _))) // the key is absent for EMPLOYED
            .ToList();
        var summary = ((string[])reader.GetValue(1)).ToList();

        var pieces = new List<(DateOnly From, DateOnly To)>();
        if (!reader.IsDBNull(2))
        {
            using var inputsDoc = JsonDocument.Parse(reader.GetString(2));
            pieces = inputsDoc.RootElement.GetProperty("pieces").EnumerateArray()
                .Select(p => (
                    DateOnly.Parse(p.GetProperty("from").GetString()!, CultureInfo.InvariantCulture),
                    DateOnly.Parse(p.GetProperty("to").GetString()!, CultureInfo.InvariantCulture)))
                .ToList();
        }

        Assert.False(await reader.ReadAsync(), $"more than one segment_manifests row for {employeeId}");
        return new ManifestRow(segments, summary, pieces);
    }

    private async Task<int> CountManifestsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>The number of segments of the employee's most recently written manifest.</summary>
    private async Task<int> LatestManifestSegmentCountAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT jsonb_array_length(segments_jsonb) FROM segment_manifests WHERE employee_id = @e ORDER BY created_at DESC LIMIT 1",
            conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountExportRecordsAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private sealed record ExportRecordRow(
        string ContentHash, string OriginalLines, string CurrentEffectiveLines, DateTime ExportedAt);

    /// <summary>June 2026's <c>payroll_export_records</c> row for the employee (asserts it exists).</summary>
    private async Task<ExportRecordRow> LoadExportRecordAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT content_hash, original_lines::text, current_effective_lines::text, exported_at
            FROM payroll_export_records
            WHERE employee_id = @e AND year = 2026 AND month = 6
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no payroll_export_records row for {employeeId}");
        return new ExportRecordRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3));
    }
}
