using System.Diagnostics;
using System.Net;
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
/// <b>Why this is deterministic (no sleeps, no hooks).</b> Connection A takes <c>FOR UPDATE</c> on
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
/// <c>PriorMonthToRecheck</c> is not threaded) → the export does not block on September's row, the
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
                $"row lock (export task status: {export.Status}).");
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
    private async Task<bool> IsExportBlockedOnApprovalRowAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
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

    /// <summary>A condition wait, not a sleep: re-evaluates <paramref name="condition"/> (each evaluation
    /// is a database round trip) until it holds, <paramref name="giveUp"/> says stop, or the timeout ends.</summary>
    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, Func<bool> giveUp)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout && !giveUp())
        {
            if (await condition())
                return true;
            await Task.Yield();
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
