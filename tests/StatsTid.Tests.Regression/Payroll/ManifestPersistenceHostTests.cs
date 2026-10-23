using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
/// S146 / TASK-14604-T (QUAL-205; rulings R-21, R-22, R-27, R-26; AC-A1, AC-A2) — the REAL Payroll host
/// refuses a calculation whose manifest could not be stored, on both calculating routes, before any payroll
/// write; and the manifest's three writes are one transaction.
///
/// <para>
/// <b>Plain language.</b> Every payroll figure must be provable later from its stored manifest (the record of
/// how the month was split and which inputs it ran on). Payroll writes that manifest, its table row and the
/// legacy period summary event together, in one database transaction. If that transaction fails, the figure is
/// not exported and no correction is written from it: the route answers 503 "retry", and nothing exists to
/// undo. These facts break the transaction on purpose, with a database trigger that rejects every
/// <c>segment_manifests</c> insert (the seam: <c>BEFORE INSERT ... RAISE EXCEPTION</c>, created in the test's
/// own database), and look at what was and was not written.
/// </para>
///
/// <para>
/// <b>Why a retry closes each refusal fact (non-vacuity).</b> "No export record" and "no correction event" are
/// zero-counts, and a zero-count proves nothing unless the same scenario writes them when it is allowed to
/// succeed. So the two route facts drop the trigger at the end and repeat the same request: it must now
/// answer 200 and write exactly what the refusal withheld. A refusal that had left a marker or moved a
/// baseline would make the retry behave differently. Likewise the atomicity fact has a positive control
/// (<see cref="ProjectionInsertSucceeds_OneOutboxRowPerStream"/>): "no outbox row" is already true of a
/// service that never writes one, so the control proves the same call, untouched by the trigger, DOES leave one
/// outbox row per stream.
/// </para>
///
/// <para>
/// <b>Harness.</b> The house host harness (<see cref="PayrollHostFactory"/> over a Testcontainers Postgres with
/// the full <c>init.sql</c>), the shared Rule Engine stub, and the delivery POST answered 200. The host runs no
/// <c>OutboxPublisher</c> (<c>PayrollHostFactory.cs</c> removes it), so a calculation's events are observed on
/// <c>outbox_events</c> and the canonical <c>events</c> table stays empty of them. June 2026 is used because it
/// opens on a Monday: its first week holds no day of May, so no previous-month approval is involved.
/// </para>
///
/// <para>
/// <b>Docker-gated: CI-pending (flips at CI-W2), never locally green.</b> Kind <b>F</b> throughout; each fact
/// names the assertion that fails on the code before TASK-14604 under "Red conditions".
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ManifestPersistenceHostTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string OrgId = "STY_S146_MANIFEST";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK26";
    private const string ExpectedKind = "payroll-manifest-not-persisted";

    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    private PostgreSqlContainer _container = null!;
    private string _connectionString = null!;
    private DbConnectionFactory _db = null!;
    private PayrollHostFactory _factory = null!;
    private long _outboxSeq = 9000;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await _container.StartAsync();

        _connectionString = _container.GetConnectionString();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_connectionString);
        _db = new DbConnectionFactory(_connectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(_db);

        _factory = new PayrollHostFactory(_connectionString, new RuleRegistry().GetAll());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-A1 — the forward route
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-A1, forward route. The manifest's projection insert fails, so the calculation is
    /// <c>NotPersisted</c>: <c>POST /api/payroll/calculate-and-export</c> answers <b>503</b> with the fixed body
    /// (exactly <c>error</c>, <c>kind</c>, <c>success</c>; no employee id, no date, no digit), and writes no
    /// export record, no <c>PayrollExportGenerated</c> outbox event, no ADR-026 audit row, no delivery envelope
    /// and no manifest; the request is still logged (<c>audit_log</c>) but carries no manifest id. The same
    /// request, repeated once the trigger is dropped, answers 200 and writes the export record (non-vacuity).
    ///
    /// <para>
    /// <b>Red conditions (F).</b> Before TASK-14604 the manifest write swallowed its failure into an
    /// <c>AuditState</c> flag nobody read, so the export proceeded: the request answers 200 (not 503), the
    /// <c>payroll_export_records</c> count is 1 (not 0), and the status assertion trips first. Mutation after the
    /// fix: remove the <c>ThrowIfNotProvable</c> call from the forward route in <c>Program.cs</c>; the same
    /// assertions trip.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_ManifestNotPersisted_Returns503_NoPayrollWrite()
    {
        const string employeeId = "EMP-S146-HOST-FWD";
        await SeedMonthAsync(employeeId);
        await CreateManifestInsertTriggerAsync();
        var host = DeliveringHost();

        var (status, json) = await PostCalculateAndExportAsync(host, employeeId);

        Assert.True(status == HttpStatusCode.ServiceUnavailable, $"expected 503, got {(int)status}: {json}");
        AssertManifestNotPersistedBody(json, employeeId);

        // No payroll write: no export record (the month's lock), no event, no delivery envelope, no audit row.
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", employeeId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'",
            ("s", $"employee-{employeeId}")));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM outbox_messages WHERE destination = 'payroll'"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'PayrollExportGenerated'"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employeeId)));

        // The failed request is logged (by design) but names no manifest: nothing stamped an id.
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE http_path = '/api/payroll/calculate-and-export' AND http_status = 503") >= 1,
            "non-vacuity: the refused request is in the request log");
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_log WHERE details->>'manifest_id' IS NOT NULL"));

        // Non-vacuity: with the trigger gone, the identical request exports.
        await DropManifestInsertTriggerAsync();
        var (retryStatus, retryJson) = await PostCalculateAndExportAsync(host, employeeId);
        Assert.True(retryStatus == HttpStatusCode.OK, $"retry expected 200, got {(int)retryStatus}: {retryJson}");
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", employeeId)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-A1 — the correction route
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-A1, correction route. The same broken manifest write under <c>POST /api/payroll/recalculate</c> for an
    /// exported June: <b>503</b> with the same fixed body, and before the correction transaction opens, so the
    /// export baseline (<c>current_effective_lines</c>) is unchanged and there is no
    /// <c>RetroactiveCorrectionRequested</c> event, no ADR-026 correction audit row, no manifest and no delivered
    /// idempotency marker. Repeating the request with the SAME idempotency token once the trigger is dropped
    /// answers 200 and writes exactly one correction event (non-vacuity: a token the refusal had marked as
    /// delivered would be answered "already processed" with no event).
    ///
    /// <para>
    /// <b>Red conditions (F).</b> Before TASK-14604 the correction route had no manifest check: the correction
    /// transaction committed on a calculation whose manifest never existed, so the request answers 200 (not 503),
    /// the baseline changes and the correction event count is 1 (not 0): the status assertion trips first.
    /// Mutation after the fix: remove the <c>ThrowIfNotProvable</c> call from
    /// <c>RetroactiveCorrectionService.RecalculateAsync</c>; the same assertions trip.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recalculate_ManifestNotPersisted_Returns503_NoPayrollWrite()
    {
        const string employeeId = "EMP-S146-HOST-RECALC";
        await SeedMonthAsync(employeeId);
        await PersistJuneExportRecordAsync(employeeId);
        var linesBefore = await ReadCurrentEffectiveLinesAsync(employeeId);
        var token = Guid.NewGuid();
        await CreateManifestInsertTriggerAsync();
        var host = DeliveringHost();

        var (status, json) = await PostRecalculateAsync(host, employeeId, token);

        Assert.True(status == HttpStatusCode.ServiceUnavailable, $"expected 503, got {(int)status}: {json}");
        AssertManifestNotPersistedBody(json, employeeId);

        // No payroll write: the baseline is untouched and there is no event, audit row, manifest or marker.
        Assert.Equal(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId));
        Assert.Equal(0, await CountCorrectionEventsAsync(employeeId));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'RetroactiveCorrectionRequested' AND target_resource_id = @e",
            ("e", employeeId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employeeId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t AND status = 'delivered'", ("t", token)));

        // Non-vacuity: with the trigger gone, the same request with the same token corrects the month.
        await DropManifestInsertTriggerAsync();
        var (retryStatus, retryJson) = await PostRecalculateAsync(host, employeeId, token);
        Assert.True(retryStatus == HttpStatusCode.OK, $"retry expected 200, got {(int)retryStatus}: {retryJson}");
        Assert.Equal(1, await CountCorrectionEventsAsync(employeeId));
        Assert.NotEqual(linesBefore, await ReadCurrentEffectiveLinesAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-A2 — one transaction
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-A2 (R-22): the manifest event's outbox row, the legacy event's outbox row and the
    /// <c>segment_manifests</c> row are one transaction. With the projection insert forced to fail, the call
    /// answers 503 and leaves <b>zero</b> <c>outbox_events</c> rows and <b>zero</b> <c>events</c> rows on
    /// <c>segment-manifest-%</c> and on <c>period-calc-%</c>: neither event survives its transaction's rollback,
    /// and neither was appended directly. Its positive control is
    /// <see cref="ProjectionInsertSucceeds_OneOutboxRowPerStream"/>.
    ///
    /// <para>
    /// <b>Red conditions (F).</b> Before TASK-14604 the manifest event is appended straight to <c>events</c> on
    /// its own connection BEFORE the projection insert, so when the projection fails the manifest event is
    /// already in <c>events</c>: the <c>events</c> count on <c>segment-manifest-%</c> is 1, not 0. Mutation after
    /// the fix: commit the manifest's enqueue in its own transaction before the projection insert (so the
    /// enqueue is not rolled back): the <c>outbox_events</c> count on <c>segment-manifest-%</c> is 1, not 0.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ProjectionInsertFails_NoOutboxOrEventRowForManifestOrLegacy()
    {
        const string employeeId = "EMP-S146-HOST-ATOMIC";
        await SeedMonthAsync(employeeId);
        await CreateManifestInsertTriggerAsync();

        var (status, json) = await PostCalculateAndExportAsync(DeliveringHost(), employeeId);

        Assert.True(status == HttpStatusCode.ServiceUnavailable, $"expected 503, got {(int)status}: {json}");
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE stream_id LIKE 'segment-manifest-%'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE stream_id LIKE 'period-calc-%'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM events WHERE stream_id LIKE 'segment-manifest-%'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM events WHERE stream_id LIKE 'period-calc-%'"));
    }

    /// <summary>
    /// The positive control of <see cref="ProjectionInsertFails_NoOutboxOrEventRowForManifestOrLegacy"/> (R-26):
    /// the same call with no trigger answers 200 and leaves exactly <b>one</b> <c>outbox_events</c> row on
    /// <c>segment-manifest-%</c> and exactly one on <c>period-calc-%</c> (the host has no publisher, so
    /// <c>events</c> holds neither), one <c>segment_manifests</c> row, and the request log row carries exactly
    /// that manifest's id (the stamp). It proves the zero-counts above are zero BECAUSE of the rollback.
    ///
    /// <para>
    /// <b>Red conditions (F).</b> Before TASK-14604 the manifest and the legacy event are appended directly, so
    /// no outbox row exists on either stream: the "one outbox row" assertion on <c>segment-manifest-%</c> finds 0.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ProjectionInsertSucceeds_OneOutboxRowPerStream()
    {
        const string employeeId = "EMP-S146-HOST-CONTROL";
        await SeedMonthAsync(employeeId);

        var (status, json) = await PostCalculateAndExportAsync(DeliveringHost(), employeeId);

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE stream_id LIKE 'segment-manifest-%'"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE stream_id LIKE 'period-calc-%'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM events WHERE stream_id LIKE 'segment-manifest-%'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM events WHERE stream_id LIKE 'period-calc-%'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", employeeId)));

        // The stamp control: the request log names exactly the manifest that was stored (so the "no manifest id"
        // assertion of the refusal fact reads a live column, not an empty one).
        Assert.Equal(1, await CountAsync(
            """
            SELECT COUNT(*) FROM audit_log al
            JOIN segment_manifests sm ON al.details->>'manifest_id' = sm.manifest_id::text
            WHERE sm.employee_id = @e
            """, ("e", employeeId)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // The seam: a trigger that rejects every segment_manifests insert
    // ═════════════════════════════════════════════════════════════════════

    private async Task CreateManifestInsertTriggerAsync() => await ExecuteAsync(
        """
        CREATE OR REPLACE FUNCTION s146_reject_manifest_insert() RETURNS trigger AS $$
        BEGIN
            RAISE EXCEPTION 'S146 test seam: segment_manifests insert refused';
        END;
        $$ LANGUAGE plpgsql;
        CREATE TRIGGER s146_reject_manifest_insert
            BEFORE INSERT ON segment_manifests
            FOR EACH ROW EXECUTE FUNCTION s146_reject_manifest_insert();
        """);

    private async Task DropManifestInsertTriggerAsync() => await ExecuteAsync(
        "DROP TRIGGER IF EXISTS s146_reject_manifest_insert ON segment_manifests;");

    // ═════════════════════════════════════════════════════════════════════
    // Host calls
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>The shared host with the post-commit delivery POST answered 200 (as the sibling host suites'
    /// delivering factories do), so a committed export reads as 200 rather than a 422 delivery refusal.</summary>
    private WebApplicationFactory<RetroactiveCorrectionService> DeliveringHost() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new DeliveringClientFactory());
        }));

    private static async Task<(HttpStatusCode Status, string Json)> PostCalculateAndExportAsync(
        WebApplicationFactory<RetroactiveCorrectionService> host, string employeeId)
    {
        var body = new
        {
            profile = Profile(employeeId),
            entries = Array.Empty<TimeEntry>(),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart = Jun01,
            periodEnd = Jun30,
            previousFlexBalance = 0m,
        };
        return await PostAsync(host, "/api/payroll/calculate-and-export", body);
    }

    private static async Task<(HttpStatusCode Status, string Json)> PostRecalculateAsync(
        WebApplicationFactory<RetroactiveCorrectionService> host, string employeeId, Guid token)
    {
        var body = new
        {
            profile = Profile(employeeId),
            entries = Array.Empty<TimeEntry>(),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart = Jun01,
            periodEnd = Jun30,
            previousFlexBalance = 0m,
            reason = "S146 host pin: correction of an exported month",
            idempotencyToken = token,
        };
        return await PostAsync(host, "/api/payroll/recalculate", body);
    }

    private static async Task<(HttpStatusCode Status, string Json)> PostAsync(
        WebApplicationFactory<RetroactiveCorrectionService> host, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        var response = await host.CreateClient().SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The fixed 503 body: exactly <c>error</c>, <c>kind</c>, <c>success</c>; the discriminator from the
    /// spec; no employee id, no date and no digit anywhere in it.</summary>
    private static void AssertManifestNotPersistedBody(string json, string employeeId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(ExpectedKind, JsonAssert.RequiredMember(root, "kind").GetString());
        Assert.False(JsonAssert.RequiredMember(root, "success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(JsonAssert.RequiredMember(root, "error").GetString()));
        Assert.Equal(
            new[] { "error", "kind", "success" },
            root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(employeeId, json);
        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", json);
        Assert.DoesNotMatch(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-", json); // no manifest id
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
            employeeId: "ROOT-S146-MANIFEST",
            name: "ROOT-S146-MANIFEST",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    /// <summary>POST <c>/api/payroll/receive</c> (the post-commit delivery) answers 200; every other outbound
    /// call goes to the shared Rule Engine stub. Same shape as the sibling host suites' delivering factories.</summary>
    private sealed class DeliveringClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler = new TestFixtures.StubHandler(request =>
            request.Method == HttpMethod.Post
            && string.Equals(request.RequestUri?.AbsolutePath, "/api/payroll/receive", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : TestFixtures.DefaultRuleEngineHandler(request));

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Seeding and reads
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>An employee employed all of June 2026, June APPROVED (the <c>/calculate-and-export</c> guard),
    /// and 7.4 recorded hours on every June weekday (the stub engine answers one NORMAL_HOURS line per entry,
    /// and the route exports only a calculation that has lines).</summary>
    private async Task SeedMonthAsync(string employeeId)
    {
        Assert.Equal(DayOfWeek.Monday, Jun01.DayOfWeek); // the opening week holds no May day: no prior-month check

        await using (var conn = _db.Create())
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(
                conn, employeeId, OrgId, AgreementCode, OkVersion, partTimeFraction: 1.0m);
        }

        await using (var conn = _db.Create())
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO approval_periods (
                    period_id, employee_id, org_id, period_start, period_end, period_type, status,
                    submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
                VALUES (
                    gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED',
                    NOW(), @emp, 'ROOT-S146-MANIFEST', NOW(), @agreement, @ok)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("org", OrgId);
            cmd.Parameters.AddWithValue("start", Jun01);
            cmd.Parameters.AddWithValue("end", Jun30);
            cmd.Parameters.AddWithValue("agreement", AgreementCode);
            cmd.Parameters.AddWithValue("ok", OkVersion);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        for (var d = Jun01; d <= Jun30; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var conn = _db.Create();
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, 7.4, @agreement, @ok, FALSE, NOW(), @outbox)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("agreement", AgreementCode);
            cmd.Parameters.AddWithValue("ok", OkVersion);
            cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
    }

    /// <summary>The first-export record of June 2026 (<c>original_lines == current_effective_lines</c>), one
    /// NORMAL_HOURS line of 100.0 h, deliberately NOT the 162.8 h the recorded hours calculate to, so a correction
    /// that runs has a non-empty difference to write.</summary>
    private async Task PersistJuneExportRecordAsync(string employeeId)
    {
        var lines = new[]
        {
            new PayrollExportLine
            {
                EmployeeId = employeeId,
                WageType = "SLS_0110",
                Hours = 100.0m,
                Amount = 0m,
                PeriodStart = Jun01,
                PeriodEnd = Jun30,
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
                @id, NULL, @emp, 2026, 6, NOW(),
                @lines::jsonb, @lines::jsonb, @hash, 'EXPORT'
            )
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.Add(new NpgsqlParameter("lines", NpgsqlDbType.Jsonb) { Value = json });
        cmd.Parameters.AddWithValue("hash", hash);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task<string> ReadCurrentEffectiveLinesAsync(string employeeId)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT current_effective_lines::text FROM payroll_export_records WHERE employee_id = @e AND year = 2026 AND month = 6",
            conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "no payroll_export_records row");
        return reader.GetString(0);
    }

    private Task<int> CountCorrectionEventsAsync(string employeeId) => CountAsync(
        "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'RetroactiveCorrectionRequested' AND stream_id LIKE @s",
        ("s", $"retro-correction-{employeeId}-%"));

#pragma warning disable CA2100 // every sql here is a hardcoded literal; values flow through parameters.
    private async Task ExecuteAsync(string sql)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
#pragma warning restore CA2100
}
