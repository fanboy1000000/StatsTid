using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.TestSupport;
using Testcontainers.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;
using StatsTid.Tests.Regression.PhaseE;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S145 / TASK-14512-T (14512 brief Do 7) — the reopen-versus-export race on the PREVIOUS month,
/// pinned deterministically against a real Postgres.
///
/// <para>
/// <b>Plain language.</b> October's first week holds September's last days, so payroll calculates
/// October only while September is APPROVED. Between the moment the calculation read September as
/// APPROVED and the moment the export commits, an admin could reopen September (back to DRAFT). Without
/// protection October would then be paid on a September that is being withdrawn. The export therefore
/// re-reads September's approval row <i>under a row lock</i> inside its own transaction; if a reopen
/// holds that row, the export waits for it, then sees the committed DRAFT and refuses — writing nothing.
/// </para>
///
/// <para>
/// <b>Scope of this fact.</b> It pins the export SERVICE's recheck <i>given the context</i>: it hands
/// <c>ExportAsync</c> a <c>PriorMonthToRecheck</c> directly. That the payroll host actually sets that
/// field on <c>/calculate-and-export</c> is pinned by the endpoint-level fact
/// <see cref="PriorMonthExportRaceHostTests"/> (<c>CalculateAndExport_PriorMonthReopenedWhileExportWaits_Returns409_CalculationKept_NoExportRecord</c>).
/// </para>
///
/// <para>
/// <b>Why this is deterministic (no hooks; a bounded condition wait, no outcome-deciding sleep).</b> Connection A takes <c>FOR UPDATE</c> on
/// September's approval row and keeps its transaction open. The export for October is started on a
/// task; its <c>FOR SHARE</c> read of that row cannot complete until A ends its transaction, whatever
/// the scheduling. The test then waits on a <i>condition</i>, not a duration: a backend whose current
/// statement is the export's <c>FOR SHARE</c> read and whose wait is a lock wait
/// (<c>pg_stat_activity.wait_event_type = 'Lock'</c>) — a bounded wait that fails loudly if the export
/// never blocks. Only then does A set September to DRAFT and commit; the export can only proceed
/// afterwards and must read the committed DRAFT. No production hook is needed.
/// </para>
///
/// Red conditions (Docker-gated — cannot run locally; CI-verified):
/// (1) mutation: the prior-month recheck in <c>PayrollExportService.ExportAsync</c> is removed (or
/// <c>ExportAsync</c> ignores the context's <c>PriorMonthToRecheck</c>) → the export does not block on September's row, the
/// "export is waiting on the lock" condition never holds and the fact fails with that message (and the
/// export would commit October).
/// (2) mutation: the recheck reads September with a plain <c>SELECT</c> (no <c>FOR SHARE</c>) → same as
/// (1): nothing waits for the reopen, the blocked-backend condition never holds.
/// (3) mutation: the recheck takes the lock but accepts any status (the status comparison dropped) →
/// the export completes after A commits; <c>Assert.ThrowsAsync&lt;PriorMonthNotApprovedException&gt;</c>
/// trips.
/// </summary>
[Trait("Category", "Docker")]
public sealed class PriorMonthExportRaceTests : IAsyncLifetime
{
    private const string OrgId = "ORG_PRIORMONTH_RACE";
    private const string EmployeeId = "EMP_PRIORMONTH_RACE";

    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private PayrollExportService _service = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);
        await PayrollExportRecordsTestSchema.ApplyAsync(_harness.ConnectionString);

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
                  VALUES (@id, 'Prior Month Race Org', 'ORGANISATION', @path)
                  ON CONFLICT DO NOTHING", conn);
            cmd.Parameters.AddWithValue("id", OrgId);
            cmd.Parameters.AddWithValue("path", $"/{OrgId}/");
            await cmd.ExecuteNonQueryAsync();
        }

        var factory = _harness.Factory;
        var eventStore = new PostgresEventStore(factory, new OutboxServiceContext("payroll"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ServiceUrls:MockPayroll"] = "http://mock-payroll-test" }).Build();
        _service = new PayrollExportService(
            new OkHttpClientFactory(),
            factory,
            eventStore,
            new AuditProjectionRepository(factory),
            new PayrollExportGeneratedAuditMapper(),
            configuration,
            NullLogger<PayrollExportService>.Instance);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Export_PriorMonthReopenedWhileExportWaits_RefusedPriorMonthNotApproved_NothingWritten()
    {
        // September and October both APPROVED: the calculation's admission would have passed.
        await SeedApprovalAsync(Sep01, Sep30);
        var octoberPeriodId = await SeedApprovalAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        var context = new PayrollExportContext
        {
            PeriodId = octoberPeriodId,
            PriorMonthToRecheck = (Sep01, Sep30),
            Source = "CALCULATE_AND_EXPORT",
            ActorId = "admin-test",
            ActorRole = "GlobalAdmin",
            ResolvedTargetOrgId = OrgId,
        };
        var octoberLines = new[]
        {
            new PayrollExportLine
            {
                EmployeeId = EmployeeId,
                WageType = "SLS_0110",
                Hours = 7.4m,
                Amount = 1000m,
                PeriodStart = new DateOnly(2026, 10, 1),
                PeriodEnd = new DateOnly(2026, 10, 31),
                OkVersion = "OK26",
                SourceTimeType = "NORMAL_HOURS",
            },
        };

        // Connection A plays the reopen: it holds September's approval row FOR UPDATE.
        await using var reopen = new NpgsqlConnection(_harness.ConnectionString);
        await reopen.OpenAsync();
        await using var reopenTx = await reopen.BeginTransactionAsync();
        Task<ExportResult>? export = null;
        var reopenCommitted = false;
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                """
                SELECT status FROM approval_periods
                WHERE employee_id = @e AND period_start = @s AND period_end = @p
                FOR UPDATE
                """, reopen, reopenTx))
            {
                lockCmd.Parameters.AddWithValue("e", EmployeeId);
                lockCmd.Parameters.AddWithValue("s", Sep01);
                lockCmd.Parameters.AddWithValue("p", Sep30);
                Assert.Equal("APPROVED", (string?)await lockCmd.ExecuteScalarAsync());
            }

            // The export for October starts; its FOR SHARE read of September cannot complete until A ends.
            export = Task.Run(() => _service.ExportAsync(octoberLines, context));

            // Wait on the CONDITION "the export is blocked on that lock" (bounded; fails loudly).
            var blocked = await WaitUntilAsync(
                IsExportBlockedOnApprovalRowAsync, TimeSpan.FromSeconds(30), () => export.IsCompleted);
            Assert.True(blocked,
                "the export never blocked on September's approval row: the prior-month recheck does not take a " +
                $"row lock (export task status: {export.Status}).{DescribeFault(export)}");
            Assert.False(export.IsCompleted, "the export finished while the reopen still held September's row.");

            // The reopen: September back to DRAFT, committed. Only now can the export's read proceed.
            await using (var reopenCmd = new NpgsqlCommand(
                """
                UPDATE approval_periods SET status = 'DRAFT'
                WHERE employee_id = @e AND period_start = @s AND period_end = @p
                """, reopen, reopenTx))
            {
                reopenCmd.Parameters.AddWithValue("e", EmployeeId);
                reopenCmd.Parameters.AddWithValue("s", Sep01);
                reopenCmd.Parameters.AddWithValue("p", Sep30);
                Assert.Equal(1, await reopenCmd.ExecuteNonQueryAsync());
            }
            await reopenTx.CommitAsync();
            reopenCommitted = true;

            var refused = await Assert.ThrowsAsync<PriorMonthNotApprovedException>(() => export);
            Assert.Equal(Sep01, refused.PeriodStart);
            Assert.Equal(Sep30, refused.PeriodEnd);
        }
        finally
        {
            // Never leave the lock held (a failed assertion above must not hang the container teardown).
            if (!reopenCommitted)
                await reopenTx.RollbackAsync();
            if (export is not null)
            {
                try { await export; } catch { /* the assertion above reports the outcome */ }
            }
        }

        // Nothing was written: no export record, no event, no outbox envelope, no audit row.
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", EmployeeId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'",
            ("s", $"employee-{EmployeeId}")));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM outbox_messages WHERE destination = 'payroll'"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'PayrollExportGenerated'"));
        // The reopen itself stands: September is DRAFT, October untouched.
        Assert.Equal("DRAFT", await StatusAsync(Sep01, Sep30));
        Assert.Equal("APPROVED", await StatusAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>True when some backend is waiting on a lock while running the export's
    /// <c>FOR SHARE</c> read of an approval row.</summary>
    private Task<bool> IsExportBlockedOnApprovalRowAsync() => IsExportBlockedOnApprovalRowAsync(_harness.ConnectionString);

    internal static async Task<bool> IsExportBlockedOnApprovalRowAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
              AND query LIKE '%approval_periods%'
              AND query LIKE '%FOR SHARE%'
            """, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= 1;
    }

    /// <summary>When the export task already FAULTED, its real exception: a wiring slip must not read as
    /// "the recheck takes no row lock".</summary>
    internal static string DescribeFault(Task task) =>
        task.IsFaulted ? $" The export faulted instead: {task.Exception?.GetBaseException()}" : string.Empty;

    /// <summary>A condition wait, not a sleep: re-evaluates <paramref name="condition"/> (each evaluation
    /// is a database round trip) until it holds, <paramref name="giveUp"/> says stop, or the timeout ends.</summary>
    internal static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, Func<bool> giveUp)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout && !giveUp())
        {
            if (await condition())
                return true;
            await Task.Delay(25); // a gentle poll interval; the CONDITION, not the duration, decides (bound: the timeout)
        }
        return await condition();
    }

    private async Task<Guid> SeedApprovalAsync(DateOnly start, DateOnly end)
    {
        var periodId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version)
            VALUES
                (@pid, @emp, @org, @start, @end, 'MONTHLY', 'APPROVED', 'AC', 'OK26')
            """, conn);
        cmd.Parameters.AddWithValue("pid", periodId);
        cmd.Parameters.AddWithValue("emp", EmployeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        await cmd.ExecuteNonQueryAsync();
        return periodId;
    }

    private async Task<string?> StatusAsync(DateOnly start, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM approval_periods WHERE employee_id = @e AND period_start = @s AND period_end = @p", conn);
        cmd.Parameters.AddWithValue("e", EmployeeId);
        cmd.Parameters.AddWithValue("s", start);
        cmd.Parameters.AddWithValue("p", end);
        return (string?)await cmd.ExecuteScalarAsync();
    }

#pragma warning disable CA2100 // every sql here is a hardcoded literal; values flow through parameters.
    private async Task<int> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
#pragma warning restore CA2100

    private sealed class OkHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new OkHandler());

        private sealed class OkHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

/// <summary>
/// S145 / TASK-14512-T (Step 5a, Codex WARNING) — the ENDPOINT-level twin of
/// <see cref="PriorMonthExportRaceTests"/>: the same reopen-versus-export race, driven through the REAL
/// Payroll host (<c>POST /api/payroll/calculate-and-export</c>), so it also pins that the host threads the
/// calculation's previous-month requirement into the export context (<c>PriorMonthToRecheck =
/// outcome.PriorMonthApprovalRequired</c>, <c>Payroll/Program.cs</c>). The service-level fact hands the
/// context in by hand and would still pass if the host stopped setting it.
///
/// <para>
/// <b>Harness.</b> The house host harness (<c>PayrollHostFactory</c> over a Testcontainers Postgres with the
/// full <c>init.sql</c>, exactly as <c>PayrollHostWeeklyTests</c> sets it up), with the shared Rule Engine
/// STUB behind it plus the delivery-POST-answers-200 swap. The stub satisfies the engine: its default
/// handler returns a <c>NORMAL_HOURS</c> line item per recorded entry, so seeding October's weekdays as
/// <c>time_entries_projection</c> rows (payroll reads its own hours; the body carries none) gives the
/// calculation export lines — without lines the host skips the export and there would be nothing to
/// race. No real engine is needed: no figure is asserted, only the status and what was written.
/// </para>
///
/// <para>
/// <b>Why it is deterministic.</b> The calculation's own prior-month check is a plain read, which a row
/// lock does not block, so it passes (September is APPROVED when it reads). The export's
/// <c>FOR SHARE</c> recheck of the same row then blocks behind connection A's <c>FOR UPDATE</c>. The test
/// waits on that condition (<c>pg_stat_activity</c>: a lock wait on a query naming <c>approval_periods</c>
/// and <c>FOR SHARE</c>; bounded, fails loudly), then sets September to DRAFT and commits A. The export can
/// only then proceed, reads DRAFT, and the host answers 409.
/// </para>
///
/// <para>
/// <b>What is kept.</b> As with the existing B2 recheck of this month's approval, the calculation's own
/// artifacts are written BEFORE the export and stay: the segment manifest (<c>segment_manifests</c> row)
/// and the legacy <c>PeriodCalculationCompleted</c> event (stream <c>period-calc-{employee}-2026-10-01</c>
/// in <c>events</c>). Both are observable from the test, by SQL. Not asserted separately: the manifest
/// event's own stream (<c>segment-manifest-{manifestId}</c>), which is the same fact as the manifest row.
/// </para>
///
/// Red conditions (Docker-gated — cannot run locally; CI-verified):
/// (1) mutation: the host's <c>PriorMonthToRecheck = outcome.PriorMonthApprovalRequired</c> assignment
/// removed (<c>Payroll/Program.cs</c>, the <c>/calculate-and-export</c> export context) → the export takes
/// no previous-month lock and never blocks; the blocked-wait assertion trips (the request instead finishes
/// 200 and exports October on a withdrawn September).
/// (2) mutation: the recheck in <c>ExportAsync</c> removed or reading with a plain <c>SELECT</c> → the same.
/// (3) mutation: the host drops its catch of the export's <c>PriorMonthNotApprovedException</c> → the 409
/// assertion trips (the in-process host has no exception middleware, so the request task faults rather than answering 500; the fact goes red either way).
/// </summary>
[Trait("Category", "Docker")]
public sealed class PriorMonthExportRaceHostTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    private const string OrgId = "STY_S145_RACE";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK26";
    private const string EmployeeId = "EMP-S145-RACE-HOST";

    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private PostgreSqlContainer _container = null!;
    private string _connectionString = null!;
    private DbConnectionFactory _db = null!;
    private PayrollHostFactory _factory = null!;
    private long _outboxSeq = 5000;

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
        await Segmentation.TestFixtures.SeedWageTypeMappingsAsync(_db);

        _factory = new PayrollHostFactory(_connectionString, new RuleRegistry().GetAll());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    [Fact]
    public async Task CalculateAndExport_PriorMonthReopenedWhileExportWaits_Returns409_CalculationKept_NoExportRecord()
    {
        Assert.Equal(DayOfWeek.Thursday, Oct01.DayOfWeek); // October's opening week reaches September
        await using (var conn = _db.Create())
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(
                conn, EmployeeId, OrgId, AgreementCode, OkVersion, partTimeFraction: 1.0m);
        }
        await SeedApprovalAsync(Sep01, Sep30);
        await SeedApprovalAsync(Oct01, Oct31);
        for (var d = Oct01; d <= Oct31; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                await SeedHoursAsync(d, 7.4m);
        }

        var host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new DeliveringClientFactory());
        }));

        // Connection A plays the reopen of September: it holds that approval row FOR UPDATE.
        await using var reopen = new NpgsqlConnection(_connectionString);
        await reopen.OpenAsync();
        await using var reopenTx = await reopen.BeginTransactionAsync();
        Task<(HttpStatusCode Status, string Json)>? response = null;
        var reopenCommitted = false;
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                """
                SELECT status FROM approval_periods
                WHERE employee_id = @e AND period_start = @s AND period_end = @p
                FOR UPDATE
                """, reopen, reopenTx))
            {
                lockCmd.Parameters.AddWithValue("e", EmployeeId);
                lockCmd.Parameters.AddWithValue("s", Sep01);
                lockCmd.Parameters.AddWithValue("p", Sep30);
                Assert.Equal("APPROVED", (string?)await lockCmd.ExecuteScalarAsync());
            }

            // The calculation's check (a plain read) passes; the export's FOR SHARE recheck then blocks.
            response = Task.Run(() => PostCalculateAndExportAsync(host));

            var blocked = await PriorMonthExportRaceTests.WaitUntilAsync(
                () => PriorMonthExportRaceTests.IsExportBlockedOnApprovalRowAsync(_connectionString),
                TimeSpan.FromSeconds(30), () => response.IsCompleted);
            var early = string.Empty;
            if (response.IsCompletedSuccessfully)
            {
                var (earlyStatus, earlyJson) = await response; // already complete: no blocking
                early = $" The request already answered {(int)earlyStatus}: {earlyJson}";
            }
            Assert.True(blocked,
                "the export never blocked on September's approval row: the host did not thread the previous " +
                "month into the export context, or the recheck takes no row lock " +
                $"(request task status: {response.Status}).{early}{PriorMonthExportRaceTests.DescribeFault(response)}");
            Assert.False(response.IsCompleted, "the request finished while the reopen still held September's row.");

            // The reopen: September back to DRAFT, committed. Only now can the export's read proceed.
            await using (var reopenCmd = new NpgsqlCommand(
                """
                UPDATE approval_periods SET status = 'DRAFT'
                WHERE employee_id = @e AND period_start = @s AND period_end = @p
                """, reopen, reopenTx))
            {
                reopenCmd.Parameters.AddWithValue("e", EmployeeId);
                reopenCmd.Parameters.AddWithValue("s", Sep01);
                reopenCmd.Parameters.AddWithValue("p", Sep30);
                Assert.Equal(1, await reopenCmd.ExecuteNonQueryAsync());
            }
            await reopenTx.CommitAsync();
            reopenCommitted = true;

            var (status, json) = await response;
            Assert.True(status == HttpStatusCode.Conflict, $"expected 409, got {(int)status}: {json}");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("payroll-prior-month-not-approved", root.GetProperty("kind").GetString());
            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.Equal("2026-09-01", root.GetProperty("periodStart").GetString());
            Assert.Equal("2026-09-30", root.GetProperty("periodEnd").GetString());
        }
        finally
        {
            if (!reopenCommitted)
                await reopenTx.RollbackAsync();
            if (response is not null)
            {
                try { await response; } catch { /* the assertions above report the outcome */ }
            }
        }

        // The export wrote nothing: no record (so no lock), no event, no delivery envelope, no audit row.
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", ("e", EmployeeId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'PayrollExportGenerated'",
            ("s", $"employee-{EmployeeId}")));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM outbox_messages WHERE destination = 'payroll'"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'PayrollExportGenerated'"));

        // The calculation's artifacts are KEPT (written before the export, as with the B2 recheck).
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", EmployeeId)));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM events WHERE stream_id = @s",
            ("s", $"period-calc-{EmployeeId}-2026-10-01")));

        // The reopen itself stands.
        Assert.Equal("DRAFT", await StatusAsync(Sep01, Sep30));
        Assert.Equal("APPROVED", await StatusAsync(Oct01, Oct31));
    }

    // ─── HTTP ────────────────────────────────────────────────────────────

    private static async Task<(HttpStatusCode Status, string Json)> PostCalculateAndExportAsync(
        WebApplicationFactory<RetroactiveCorrectionService> host)
    {
        var body = new
        {
            profile = new EmploymentProfile
            {
                EmployeeId = EmployeeId,
                AgreementCode = AgreementCode,
                OkVersion = OkVersion,
                EmploymentCategory = "Standard",
                PartTimeFraction = 1.0m,
                OrgId = OrgId,
            },
            entries = Array.Empty<TimeEntry>(),
            absences = Array.Empty<AbsenceEntry>(),
            periodStart = Oct01,
            periodEnd = Oct31,
            previousFlexBalance = 0m,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        var httpResponse = await host.CreateClient().SendAsync(request);
        return (httpResponse.StatusCode, await httpResponse.Content.ReadAsStringAsync());
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
            employeeId: "ROOT-S145-RACE",
            name: "ROOT-S145-RACE",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    /// <summary>POST /api/payroll/receive (the post-commit delivery) answers 200; every other outbound
    /// call goes to the shared Rule Engine stub. Same shape as the host suites' delivering factory.</summary>
    private sealed class DeliveringClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler = new Segmentation.TestFixtures.StubHandler(request =>
            request.Method == HttpMethod.Post
            && string.Equals(request.RequestUri?.AbsolutePath, "/api/payroll/receive", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : Segmentation.TestFixtures.DefaultRuleEngineHandler(request));

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    // ─── Seeding and reads ───────────────────────────────────────────────

    private async Task SeedApprovalAsync(DateOnly periodStart, DateOnly periodEnd)
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
                NOW(), @emp, 'ROOT-S145-RACE', NOW(), @agreement, @ok)
            """, conn);
        cmd.Parameters.AddWithValue("emp", EmployeeId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("start", periodStart);
        cmd.Parameters.AddWithValue("end", periodEnd);
        cmd.Parameters.AddWithValue("agreement", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>One RAW time-entry projection row (a current TIME_POST row), the shape the real-engine host
    /// facts seed.</summary>
    private async Task SeedHoursAsync(DateOnly date, decimal hours)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO time_entries_projection
                (event_id, employee_id, date, hours, agreement_code, ok_version,
                 voluntary_unsocial_hours, occurred_at, outbox_id)
            VALUES
                (gen_random_uuid(), @emp, @date, @hours, @agreement, @ok, FALSE, NOW(), @outbox)
            """, conn);
        cmd.Parameters.AddWithValue("emp", EmployeeId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("agreement", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("outbox", _outboxSeq++);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task<string?> StatusAsync(DateOnly start, DateOnly end)
    {
        await using var conn = _db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM approval_periods WHERE employee_id = @e AND period_start = @s AND period_end = @p", conn);
        cmd.Parameters.AddWithValue("e", EmployeeId);
        cmd.Parameters.AddWithValue("s", start);
        cmd.Parameters.AddWithValue("p", end);
        return (string?)await cmd.ExecuteScalarAsync();
    }

#pragma warning disable CA2100 // every sql here is a hardcoded literal; values flow through parameters.
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
