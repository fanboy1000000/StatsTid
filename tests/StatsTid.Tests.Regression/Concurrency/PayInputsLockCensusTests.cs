using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// S148 / TASK-14821-T (AC-PIL-e row (1); ADR-034 D8's ledger column) - the pay-inputs lock is taken first
/// and alone by the Backend writers that date a pay input into the past, and by nobody else.
///
/// <para>
/// <b>In plain language.</b> Payroll first calculates a month and then records it. A correction dated back
/// into that month must take turns with the recording, so every Backend writer of a pay input takes one
/// shared "pay inputs" lock as its very first step. "First and alone" matters: a writer that held some other
/// lock while waiting for this one could form a circle with another request and the whole system would stall.
/// Each fact below holds the lock itself, starts a request, waits until PostgreSQL shows that request waiting
/// on exactly that lock, and then checks from PostgreSQL's own lock table that the waiting request holds
/// nothing except its own transaction id. Two negatives prove the lock is not taken where it should not be.
/// </para>
///
/// <para>
/// <b>Why nothing here is timing.</b> The test, not a race, decides the order: it holds the lock before the
/// request starts, and PostgreSQL grants a waiting lock in queue order. "Observed waiting" is a bounded wait on
/// a condition PostgreSQL reports (<c>pg_locks</c>), never a sleep. A negative proves "does not take the lock" by
/// completing WHILE the test still holds it, which a request that takes the lock cannot do.
/// </para>
///
/// <para>
/// <b>The serial-collections premise (ruling 5.6; every lock fact depends on it).</b> The key is system-wide, so
/// these facts are safe only because the Regression suite runs one test collection at a time
/// (<c>xunit.runner.json:3</c>, <c>parallelizeTestCollections</c> false): one class's held lock never meets another
/// class's request. Each fact also has its own database (a fresh container), so a request left running by another
/// fact cannot be the one waiter.
/// </para>
///
/// <para>
/// <b>Machinery.</b> The held lock uses the literal key <c>(148, 1)</c>, never a production constant, so a wrong key
/// in production fails the fact. In <c>pg_locks</c> the key is <c>locktype = 'advisory'</c>, <c>classid = 148</c>,
/// <c>objid = 1</c>, <c>objsubid = 2</c> (the two-integer form), requested as <c>ExclusiveLock</c>.
/// <see cref="WaitingPidsSql"/> returns the waiting backend's pid (one row asserted: one waiter);
/// <see cref="HoldsSql"/> lists that pid's locks: among the granted ones only <c>virtualxid</c>, and exactly one
/// ungranted lock, the pay-inputs key. TASK-14823-T (row (2)) and TASK-14831-T (row (3), the full census) add their
/// facts to this file; the machinery stays private here.
/// </para>
///
/// <para>
/// <b>Fixture.</b> A Backend host with the clock fixed at Thursday 5 March 2026 and the settlement go-live date
/// 1 January 2025 (the <c>SettlementReversalEndpointTests</c> supersede precedent: leaver ending 28 February 2026,
/// ferieår 2025, a TERMINATION settlement, a correction to 31 December 2025). The actor is a LocalHR scoped to STY01
/// and STY02 and is not the subject. Docker-gated: <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayInputsLockCensusTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 3, 5);
    private static readonly DateOnly LeaverEnd = new(2026, 2, 28);
    private static readonly DateOnly GoLive = new(2025, 1, 1);
    private const int LeaverFerieaar = 2025;
    private const string ActorId = "hr_s148_pil_census";

    /// <summary>The waiters on the pay-inputs key: <c>(148, 1)</c> in the two-integer key space.</summary>
    private const string WaitingPidsSql =
        "SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND classid = 148 AND objid = 1 AND objsubid = 2 AND NOT granted";

    /// <summary>Everything one backend holds or waits for. The three key columns are NULL on a <c>virtualxid</c> row
    /// (the one lock every waiting request holds), so they are read through <c>COALESCE</c>: a bare read would make
    /// <c>GetInt64</c> throw on a correct writer.</summary>
    private const string HoldsSql =
        "SELECT locktype, mode, granted, COALESCE(classid::bigint, -1), COALESCE(objid::bigint, -1), COALESCE(objsubid::int, -1) FROM pg_locks WHERE pid = @pid";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    private string Cs => _harness.ConnectionString;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
        // No leaver-flip job: a leaver seeded below must not be settled or flipped by a poll while a fact holds the lock.
        _factory = new StatsTidWebApplicationFactory(Cs, withoutLeaverJob: true);
        _host = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Settlement:GoLiveDate"] = GoLive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                }));
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Today)));
        });
        _ = _host.CreateClient(); // boot seeders before any fact seeds
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ─── the positives: each writer takes the lock first and holds nothing else while it waits ───

    /// <summary>
    /// <b>F (CI).</b> The user edit that carries an organisation (a transfer STY01 to STY02 of someone who
    /// manages nobody) waits on <c>(148, 1)</c>, holds only its virtual transaction id, and completes 200 once
    /// the lock is released.
    /// <para><b>Red conditions (on <c>ac47d9f1</c>, no writer takes <c>(148, 1)</c>).</b> The request completes
    /// 200 before it is observed waiting: <c>Assert.True(observed)</c> in <see cref="AssertWaitsFirstAsync"/> fails.
    /// A request that took another lock first would fail the granted-locks assertion.</para>
    /// </summary>
    [Fact]
    public async Task UserEdit_CarryingOrganisationChange_WaitsFirst_HoldsNothingElse()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(_host, ActorId, "STY01", "STY02");

        await AssertWaitsFirstAsync(
            () => PutUserAsync(client, Cs, person, new { primaryOrgId = "STY02" }),
            HttpStatusCode.OK, "the user edit carrying an organisation");
    }

    /// <summary>
    /// <b>F (CI).</b> The user edit that carries an agreement code (organisation absent) waits on the lock.
    /// <para><b>Red conditions.</b> As above: no writer takes the lock, so the request completes before it is
    /// observed waiting.</para>
    /// </summary>
    [Fact]
    public async Task UserEdit_CarryingAgreementCode_WaitsFirst_HoldsNothingElse()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertWaitsFirstAsync(
            () => PutUserAsync(client, Cs, person, new { agreementCode = "HK", effectiveFrom = "2026-03-05" }),
            HttpStatusCode.OK, "the user edit carrying an agreement code");
    }

    /// <summary><b>F (CI).</b> <c>PUT /api/admin/users/{id}/agreement-code</c> waits on the lock.
    /// <para><b>Red conditions.</b> As above.</para></summary>
    [Fact]
    public async Task DedicatedAgreementCodeRoute_WaitsFirst_HoldsNothingElse()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertWaitsFirstAsync(
            async () => await SendAsync(client, HttpMethod.Put, $"/api/admin/users/{person}/agreement-code",
                new { agreementCode = "HK", effectiveFrom = "2026-03-05" }, await VersionAsync(person)),
            HttpStatusCode.OK, "the dedicated agreement-code route");
    }

    /// <summary><b>F (CI).</b> <c>PUT /api/admin/employee-profiles/{id}</c> (the fraction 1.0 to 0.8, dated today)
    /// waits on the lock, before the employee lock and before any profile row lock.
    /// <para><b>Red conditions.</b> As above (and BC-11: a statement sent before the lock would show up as a granted
    /// <c>relation</c> lock and fail the granted-locks assertion, a production defect to report, never to loosen).</para></summary>
    [Fact]
    public async Task EmployeeProfilePut_WaitsFirst_HoldsNothingElse()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertWaitsFirstAsync(
            async () => await SendAsync(client, HttpMethod.Put, $"/api/admin/employee-profiles/{person}",
                new { effectiveFrom = "2026-03-05", partTimeFraction = 0.8m, position = (string?)null, employmentCategory = (string?)null },
                await VersionAsync(person)),
            HttpStatusCode.OK, "the employee-profile PUT");
    }

    /// <summary><b>F (CI).</b> <c>DELETE /api/admin/employee-profiles/{id}</c> (the profile close) waits on the lock;
    /// the transaction is at explicit READ COMMITTED, so nothing but the virtual transaction id is held.
    /// <para><b>Red conditions.</b> As above.</para></summary>
    [Fact]
    public async Task ProfileClose_WaitsFirst_HoldsNothingElse()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertWaitsFirstAsync(
            async () => await SendAsync(client, HttpMethod.Delete, $"/api/admin/employee-profiles/{person}",
                body: null, await VersionAsync(person)),
            HttpStatusCode.NoContent, "the profile close");
    }

    /// <summary><b>F (CI).</b> The employment start-date PUT (none to Monday 5 January 2026) waits on the lock.
    /// <para><b>Red conditions.</b> As above.</para></summary>
    [Fact]
    public async Task EmploymentStartDatePut_WaitsFirst_HoldsNothingElse()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertWaitsFirstAsync(
            async () => await SendAsync(client, HttpMethod.Put, $"/api/admin/employees/{person}/employment-start-date",
                new { employmentStartDate = "2026-01-05" }, await VersionAsync(person)),
            HttpStatusCode.OK, "the employment start-date PUT");
    }

    /// <summary><b>F (CI).</b> The employment end-date PUT (none to Thursday 31 December 2026) waits on the lock.
    /// <para><b>Red conditions.</b> As above.</para></summary>
    [Fact]
    public async Task EmploymentEndDatePut_WaitsFirst_HoldsNothingElse()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertWaitsFirstAsync(
            async () => await SendAsync(client, HttpMethod.Put, $"/api/admin/employees/{person}/employment-end-date",
                new { employmentEndDate = "2026-12-31" }, await VersionAsync(person)),
            HttpStatusCode.OK, "the employment end-date PUT");
    }

    /// <summary><b>F (CI).</b> A settlement reversal that carries an end-date correction (reverse-and-supersede,
    /// the leaver's end date 28 February 2026 corrected to 31 December 2025) waits on the lock, before the employee
    /// lock and the users row lock.
    /// <para><b>Red conditions.</b> As above: <c>SettlementReversalService</c> takes no <c>(148, 1)</c> lock, so the
    /// reversal completes 200 before it is observed waiting.</para></summary>
    [Fact]
    public async Task SettlementReversalWithEndDateCorrection_WaitsFirst_HoldsNothingElse()
    {
        var leaver = await SeedSettledLeaverAsync();
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertWaitsFirstAsync(
            () => SendAsync(client, HttpMethod.Post, $"/api/admin/employees/{leaver}/settlement-reversal", new
            {
                entitlementYear = LeaverFerieaar,
                expectedSettlementSequence = 1,
                mode = "REVERSE_AND_SUPERSEDE",
                hasEndDateCorrection = true,
                correctedEndDate = "2025-12-31",
                expectedUserVersion = 1,
            }, 1L),
            HttpStatusCode.OK, "the settlement reversal with an end-date correction");
    }

    // ─── the negatives: the lock is not taken where it should not be ───

    /// <summary>
    /// <b>R.</b> A user edit with no organisation and no agreement code (a display-name change) completes 200
    /// WHILE the test holds the lock.
    /// <para><b>Red conditions.</b> Taking the lock unconditionally (the user edit's guard
    /// <c>request.PrimaryOrgId is not null || request.AgreementCode is not null</c> removed) makes the request
    /// wait: <c>AssertCompletesWhileHeldAsync</c> fails with "still waiting".</para>
    /// </summary>
    [Fact]
    public async Task UserEdit_CarryingNeither_DoesNotWait()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertCompletesWhileHeldAsync(
            () => PutUserAsync(client, Cs, person, new { displayName = "S148 Renamed Under The Lock" }),
            HttpStatusCode.OK, "the display-name edit");
    }

    /// <summary>
    /// <b>R.</b> A bare settlement reversal (no end-date correction) completes 200 WHILE the test holds the lock.
    /// <para><b>Red conditions.</b> Taking the lock for a bare reversal (the service's
    /// <c>command.HasEndDateCorrection</c> condition dropped) makes the request wait:
    /// <c>AssertCompletesWhileHeldAsync</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task BareSettlementReversal_DoesNotWait()
    {
        var leaver = await SeedSettledLeaverAsync();
        var client = LocalHrClient(_host, ActorId, "STY01");

        await AssertCompletesWhileHeldAsync(
            () => SendAsync(client, HttpMethod.Post, $"/api/admin/employees/{leaver}/settlement-reversal",
                new { entitlementYear = LeaverFerieaar, expectedSettlementSequence = 1, mode = "BARE" }, 1L),
            HttpStatusCode.OK, "the bare settlement reversal");
    }

    // ─── machinery (private; rows (2) and (3) of AC-PIL-e add their facts to this class) ───

    /// <summary>The held lock: a connection and open transaction that ran <c>pg_advisory_xact_lock(148, 1)</c>,
    /// released by commit and always by rollback on dispose, so a failed assertion never leaves it held.</summary>
    private sealed class HeldLock : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly NpgsqlTransaction _tx;
        private bool _released;

        private HeldLock(NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            _conn = conn;
            _tx = tx;
        }

        public static async Task<HeldLock> AcquireAsync(string connectionString)
        {
            var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await using var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(148, 1)", conn, tx);
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

    private async Task<IReadOnlyList<int>> WaitingPidsAsync()
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(WaitingPidsSql, conn);
        var pids = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            pids.Add(reader.GetInt32(0));
        return pids;
    }

    private async Task AssertWaitsFirstAsync(
        Func<Task<HttpResponseMessage>> send, HttpStatusCode success, string who)
    {
        await using var held = await HeldLock.AcquireAsync(Cs);
        var request = send();

        var observed = await PriorMonthExportRaceTests.WaitUntilAsync(
            async () => (await WaitingPidsAsync()).Count > 0, TimeSpan.FromSeconds(30), () => request.IsCompleted);
        var early = request.IsCompletedSuccessfully
            ? $" The request already answered {(int)request.Result.StatusCode}: {await request.Result.Content.ReadAsStringAsync()}"
            : string.Empty;
        Assert.True(observed,
            $"{who} was never observed waiting on the pay-inputs lock (148, 1) (request status: {request.Status}). " +
            $"No writer takes the lock here, or the request ended before it.{early}" +
            PriorMonthExportRaceTests.DescribeFault(request));
        Assert.False(request.IsCompleted, $"{who} finished while the pay-inputs lock was still held.");

        var pids = await WaitingPidsAsync();
        var pid = Assert.Single(pids); // one waiter: the key is system-wide (ruling 5.6)

        await AssertHoldsNothingElseAsync(pid, who);

        await held.ReleaseAsync();
        var rsp = await request;
        Assert.True(rsp.StatusCode == success,
            $"{who} should answer {(int)success} once the lock is released, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");
    }

    /// <summary>For the waiting backend: among the granted locks only <c>virtualxid</c> (no relation, tuple,
    /// transactionid, page or other advisory row), and exactly one ungranted lock: the pay-inputs key.</summary>
    private async Task AssertHoldsNothingElseAsync(int pid, string who)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(HoldsSql, conn);
        cmd.Parameters.AddWithValue("pid", pid);
        var granted = new List<string>();
        var waiting = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = $"{reader.GetString(0)}/{reader.GetString(1)}/{reader.GetInt64(3)}/{reader.GetInt64(4)}/{reader.GetInt32(5)}";
            (reader.GetBoolean(2) ? granted : waiting).Add(row);
        }

        Assert.True(granted.All(g => g.StartsWith("virtualxid/", StringComparison.Ordinal)),
            $"{who} holds a lock other than its virtual transaction id while waiting for the pay-inputs lock: {string.Join("; ", granted)}");
        var only = Assert.Single(waiting);
        Assert.Equal("advisory/ExclusiveLock/148/1/2", only);
    }

    private async Task AssertCompletesWhileHeldAsync(
        Func<Task<HttpResponseMessage>> send, HttpStatusCode success, string who)
    {
        await using var held = await HeldLock.AcquireAsync(Cs);
        var request = send();

        var completed = await PriorMonthExportRaceTests.WaitUntilAsync(
            () => Task.FromResult(request.IsCompleted), TimeSpan.FromSeconds(30), () => false);
        Assert.True(completed, $"{who} is still waiting while the pay-inputs lock is held: it takes the lock.");
        var rsp = await request;
        Assert.True(rsp.StatusCode == success,
            $"{who} should answer {(int)success}, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");
        Assert.Empty(await WaitingPidsAsync());
    }

    // ─── fixtures ───

    private Task<long> VersionAsync(string userId) =>
        ScalarLongAsync(Cs, "SELECT version FROM users WHERE user_id = @u", ("u", userId));

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, object? body, long ifMatchVersion)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        return await client.SendAsync(req);
    }

    /// <summary>A leaver ending 28 February 2026 (users.version stays 1: a direct seed) with a SETTLED TERMINATION
    /// settlement for ferieår 2025 written by the real settlement service, as the reversal endpoint fixtures do.</summary>
    private async Task<string> SeedSettledLeaverAsync()
    {
        var employeeId = await SeedPersonAsync(Cs, "STY01");
        await using (var conn = new NpgsqlConnection(Cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                                 end_date_deactivated = TRUE, updated_at = NOW()
                WHERE user_id = @id
                """, conn);
            cmd.Parameters.AddWithValue("id", employeeId);
            cmd.Parameters.AddWithValue("endDate", LeaverEnd);
            await cmd.ExecuteNonQueryAsync();
        }

        var service = _host.Services.GetRequiredService<VacationSettlementService>();
        await using var settleConn = _host.Services.GetRequiredService<DbConnectionFactory>().Create();
        await settleConn.OpenAsync();
        await using var tx = await settleConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await service.SettleAsync(employeeId, "VACATION", LeaverFerieaar, "TERMINATION", settleConn, tx);
        await tx.CommitAsync();
        Assert.True(outcome.DidSettle, "the TERMINATION settle drive must produce the settled row");
        return employeeId;
    }
}
