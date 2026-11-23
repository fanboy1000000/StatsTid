using System.Data;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// S148 / TASK-14831-T (ruling 17) - the one shared kit of the pay-inputs lock facts of
/// <see cref="PayInputsLockRaceTests"/> and <c>PayInputsLockTimingTests</c>: the held lock, the waiter query and a
/// payroll host that records what it asks the Rule Engine. <c>PayInputsLockCensusTests</c> keeps its own private
/// machinery (the file is grown by addition only) and uses only the payroll side of this kit.
///
/// <para>
/// <b>In plain language.</b> A fact that wants to decide which of two requests goes first takes the system-wide
/// "pay inputs" lock itself, starts the first request, waits until PostgreSQL shows that request waiting for the lock,
/// starts the second, waits until it shows two, and lets go. PostgreSQL grants a waiting lock in queue order, so the
/// test, not timing, decides who goes first. Nothing here is a sleep that decides an outcome: every wait is a bounded
/// wait on a condition PostgreSQL reports (<c>pg_locks</c>), which gives up when the request it watches has already
/// answered.
/// </para>
///
/// <para>
/// <b>The key is a literal.</b> <c>(148, 1)</c> in the two-integer key space; in <c>pg_locks</c> it is
/// <c>locktype = 'advisory'</c>, <c>classid = 148</c>, <c>objid = 1</c>, <c>objsubid = 2</c>. It is never read through
/// a production constant, so a wrong key in production fails the facts. The serial premise (one test collection at a
/// time, <c>xunit.runner.json:3</c>) and a fresh database per fact are the classes'.
/// </para>
/// </summary>
internal static class PayInputsLockTestKit
{
    /// <summary>The waiters on the pay-inputs key, any mode.</summary>
    internal const string WaitingPidsSql =
        "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND classid = 148 AND objid = 1 AND objsubid = 2 AND NOT granted";

    /// <summary>Granted holders of the pay-inputs key.</summary>
    internal const string HoldersSql =
        "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND classid = 148 AND objid = 1 AND objsubid = 2 AND granted";

    /// <summary>Waiters on any single-key advisory lock (the per-employee lock is one: <c>hashtext('employee-' || id)</c>).</summary>
    internal const string WaitingOnSingleKeySql =
        "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND objsubid = 1 AND NOT granted";

    internal static readonly TimeSpan WaitBound = TimeSpan.FromSeconds(30);

    // ─── the held lock ────────────────────────────────────────────────────

    /// <summary>A connection and open READ COMMITTED transaction that took one advisory lock, released by commit and
    /// always rolled back on dispose, so a failed assertion never leaves it held.</summary>
    internal sealed class HeldLock : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly NpgsqlTransaction _tx;
        private bool _released;

        private HeldLock(NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            _conn = conn;
            _tx = tx;
        }

        /// <summary>The pay-inputs lock <c>(148, 1)</c>, exclusive.</summary>
        public static Task<HeldLock> AcquirePayInputsAsync(string connectionString) =>
            AcquireAsync(connectionString, "SELECT pg_advisory_xact_lock(148, 1)", null);

        /// <summary>The per-employee lock, taken the way <c>EmployeeConsumptionLock</c> takes it.</summary>
        public static Task<HeldLock> AcquireEmployeeAsync(string connectionString, string employeeId) =>
            AcquireAsync(connectionString, "SELECT pg_advisory_xact_lock(hashtext('employee-' || @e))", employeeId);

        private static async Task<HeldLock> AcquireAsync(string connectionString, string sql, string? employeeId)
        {
            var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
#pragma warning disable CA2100 // each sql is a literal of this class; the one value flows through a parameter (QUAL-073).
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
#pragma warning restore CA2100
            if (employeeId is not null)
                cmd.Parameters.AddWithValue("e", employeeId);
            await cmd.ExecuteNonQueryAsync();
            return new HeldLock(conn, tx);
        }

        public async Task ReleaseAsync()
        {
            if (_released)
                return;
            _released = true;
            await _tx.CommitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                await _tx.RollbackAsync();
            }
            await _tx.DisposeAsync();
            await _conn.DisposeAsync();
        }
    }

    // ─── what PostgreSQL shows ────────────────────────────────────────────

    internal static async Task<IReadOnlyList<int>> PidsAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // the sql is one of this class's literals.
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        var pids = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pids.Add(reader.GetInt32(0));
        return pids;
    }

    internal static Task<IReadOnlyList<int>> WaitingPidsAsync(string connectionString) =>
        PidsAsync(connectionString, WaitingPidsSql);

    /// <summary>A bounded condition wait: true once at least <paramref name="count"/> backends wait on the pay-inputs key;
    /// false when the bound ends or <paramref name="giveUp"/> says the request being watched already answered.</summary>
    internal static Task<bool> WaitForWaitersAsync(string connectionString, int count, Func<bool> giveUp) =>
        PriorMonthExportRaceTests.WaitUntilAsync(
            async () => (await WaitingPidsAsync(connectionString)).Count >= count, WaitBound, giveUp);

    /// <summary>The wait every race fact makes after it starts a request: it shows as a waiter (and is still waiting),
    /// with the failure text that tells "no lock taken" from "the request ended or faulted first".</summary>
    internal static async Task AssertWaitingAsync(
        string connectionString, int expectedWaiters, Task<HttpResponseMessage> request, string who)
    {
        var observed = await WaitForWaitersAsync(connectionString, expectedWaiters, () => request.IsCompleted);
        var early = request.IsCompletedSuccessfully
            ? $" The request already answered {(int)request.Result.StatusCode}: {await request.Result.Content.ReadAsStringAsync()}"
            : string.Empty;
        Assert.True(observed,
            $"{who} was never observed waiting on the pay-inputs lock (148, 1) (waiters expected: {expectedWaiters}; " +
            $"request status: {request.Status}). Nothing takes the lock here, or the request ended before it.{early}" +
            PriorMonthExportRaceTests.DescribeFault(request));
        Assert.False(request.IsCompleted, $"{who} finished while the pay-inputs lock was still held.");
        Assert.Equal(expectedWaiters, (await WaitingPidsAsync(connectionString)).Count);
    }

    // ─── a payroll host that records what it asks the Rule Engine ─────────

    /// <summary>The Rule Engine stand-in of a payroll host: every call is answered by the shared stub
    /// (<see cref="TestFixtures.DefaultRuleEngineHandler"/>), the post-commit delivery POST is answered 200, and the body of
    /// every <c>evaluate-timeline</c> call is kept, so a fact can read which configuration each piece was sent with.</summary>
    internal sealed class RecordingEngine : IHttpClientFactory
    {
        private const string DeliveryPath = "/api/payroll/receive";
        private readonly List<string> _timelineBodies = new();
        private readonly HttpMessageHandler _handler;

        public RecordingEngine()
        {
            _handler = new TestFixtures.StubHandler(request =>
            {
                var path = request.RequestUri?.AbsolutePath ?? string.Empty;
                if (request.Method == HttpMethod.Post && string.Equals(path, DeliveryPath, StringComparison.Ordinal))
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                if (path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal))
                {
                    var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}";
                    lock (_timelineBodies)
                        _timelineBodies.Add(body);
                }
                return TestFixtures.DefaultRuleEngineHandler(request);
            });
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

        public void Clear()
        {
            lock (_timelineBodies)
                _timelineBodies.Clear();
        }

        /// <summary>The pieces of the latest timeline call: (from, to, the <c>weeklyNormHours</c> of the configuration it was sent with).</summary>
        public IReadOnlyList<(DateOnly From, DateOnly To, decimal WeeklyNormHours)> LatestTimelinePieces()
        {
            string latest;
            lock (_timelineBodies)
            {
                Assert.True(_timelineBodies.Count > 0, "the calculation made no evaluate-timeline call.");
                latest = _timelineBodies[^1];
            }
            using var doc = JsonDocument.Parse(latest);
            return doc.RootElement.GetProperty("pieces").EnumerateArray()
                .Select(p => (
                    DateOnly.Parse(p.GetProperty("from").GetString()!, CultureInfo.InvariantCulture),
                    DateOnly.Parse(p.GetProperty("to").GetString()!, CultureInfo.InvariantCulture),
                    p.GetProperty("config").GetProperty("weeklyNormHours").GetDecimal()))
                .ToList();
        }
    }

    /// <summary>The real payroll host (<see cref="PayrollHostFactory"/>) over a fact's database, with a GlobalAdmin
    /// caller and a <see cref="RecordingEngine"/>. Disposing it disposes both factories.</summary>
    internal sealed class PayrollHost : IAsyncDisposable
    {
        private readonly PayrollHostFactory _root;
        private readonly WebApplicationFactory<RetroactiveCorrectionService> _host;

        private PayrollHost(
            PayrollHostFactory root, WebApplicationFactory<RetroactiveCorrectionService> host, RecordingEngine engine, HttpClient client)
        {
            _root = root;
            _host = host;
            Engine = engine;
            Client = client;
        }

        public RecordingEngine Engine { get; }

        public HttpClient Client { get; }

        /// <summary>A payroll host over <paramref name="connectionString"/>. A connection string with a small
        /// <c>Maximum Pool Size</c> gives the host that pool (its own pool: the pool is keyed by the string).</summary>
        public static PayrollHost Start(string connectionString)
        {
            var root = new PayrollHostFactory(connectionString, new RuleRegistry().GetAll());
            var engine = new RecordingEngine();
            var host = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(engine);
            }));
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
            return new PayrollHost(root, host, engine, client);
        }

        /// <summary><c>POST /api/payroll/calculate-and-export</c> for one whole month of an HK / OK24 employee.</summary>
        public Task<HttpResponseMessage> ExportAsync(string employeeId, string orgId, DateOnly start, DateOnly end, string agreementCode = "HK") =>
            Client.PostAsJsonAsync("/api/payroll/calculate-and-export", new
            {
                profile = ProfileOf(employeeId, orgId, agreementCode),
                entries = Array.Empty<TimeEntry>(),
                absences = Array.Empty<AbsenceEntry>(),
                periodStart = start,
                periodEnd = end,
            });

        /// <summary><c>POST /api/payroll/recalculate</c> (the correction) of one whole month.</summary>
        public Task<HttpResponseMessage> RecalculateAsync(
            string employeeId, string orgId, DateOnly start, DateOnly end, Guid idempotencyToken, string agreementCode = "HK") =>
            Client.PostAsJsonAsync("/api/payroll/recalculate", new
            {
                profile = ProfileOf(employeeId, orgId, agreementCode),
                entries = Array.Empty<TimeEntry>(),
                absences = Array.Empty<AbsenceEntry>(),
                periodStart = start,
                periodEnd = end,
                reason = "S148 pay-inputs lock fact",
                idempotencyToken,
            });

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.DisposeAsync();
            await _root.DisposeAsync();
        }

        private static EmploymentProfile ProfileOf(string employeeId, string orgId, string agreementCode) => new()
        {
            EmployeeId = employeeId,
            AgreementCode = agreementCode,
            OkVersion = "OK24",
            EmploymentCategory = "Standard",
            PartTimeFraction = 1.0m,
            OrgId = orgId,
        };
    }

    private static string MintGlobalAdminToken() =>
        new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: "ROOT-S148-PIL",
            name: "ROOT-S148-PIL",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    // ─── a month ready to be calculated and recorded ──────────────────────

    private static long _outboxSeq = 900_000;

    /// <summary>One APPROVED MONTHLY approval row (approved by someone other than its sender and its employee, so the
    /// four-eyes checkpoint passes), for an HK / OK24 employee.</summary>
    internal static async Task SeedApprovedMonthAsync(
        string connectionString, string employeeId, string orgId, DateOnly start, DateOnly end, string agreementCode = "HK")
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (
                period_id, employee_id, org_id, period_start, period_end, period_type, status,
                submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
            VALUES (
                gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED',
                NOW(), @emp, 'ROOT-S148-PIL', NOW(), @ac, 'OK24')
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", orgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>One raw 7.4-hour time-entry projection row for each weekday of <paramref name="start"/> to
    /// <paramref name="end"/> (payroll reads its own hours; the request body carries none).</summary>
    internal static async Task SeedWeekdayHoursAsync(
        string connectionString, string employeeId, DateOnly start, DateOnly end, string agreementCode = "HK")
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES
                    (gen_random_uuid(), @emp, @date, 7.4, @ac, 'OK24', FALSE, NOW(), @outbox)
                """, conn);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("ac", agreementCode);
            cmd.Parameters.AddWithValue("outbox", Interlocked.Increment(ref _outboxSeq));
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
    }

    /// <summary>An HK / OK24 employee of <paramref name="orgId"/> (see the overload with an agreement code).</summary>
    internal static Task<string> SeedPayableEmployeeAsync(
        string connectionString, string orgId, params (DateOnly Start, DateOnly End)[] approvedMonths) =>
        SeedPayableEmployeeAsync(connectionString, orgId, "HK", approvedMonths);

    /// <summary>An employee of <paramref name="orgId"/> on <paramref name="agreementCode"/> / OK24 (<c>RegressionSeed</c>:
    /// users, profile, agreement code, organisation row) with each month of <paramref name="approvedMonths"/> APPROVED and
    /// its weekdays' hours recorded. The wage-type mappings the export needs are seeded by <c>init.sql</c> and by the shared
    /// harness seed.</summary>
    internal static async Task<string> SeedPayableEmployeeAsync(
        string connectionString, string orgId, string agreementCode, params (DateOnly Start, DateOnly End)[] approvedMonths)
    {
        var employeeId = "usr_s148_pil_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(connectionString, employeeId, orgId, agreementCode, "OK24");
        foreach (var (start, end) in approvedMonths)
        {
            await SeedApprovedMonthAsync(connectionString, employeeId, orgId, start, end, agreementCode);
            await SeedWeekdayHoursAsync(connectionString, employeeId, start, end, agreementCode);
        }
        return employeeId;
    }
}
