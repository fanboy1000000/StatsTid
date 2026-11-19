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

    // ─── row (2) (TASK-14823-T): the six configuration writes wait first and hold nothing else ───
    //
    // The actor is a GlobalAdmin (the five configuration routes are GlobalAdminOnly or, for the local profile,
    // LocalAdminOrAbove). Everything a request needs before its transaction (a published predecessor, a draft, a
    // deactivation) is done through the real routes BEFORE the test takes the lock; the dates are on or before the
    // class clock (Thursday 5 March 2026) and a Monday where a Monday-only field could change.

    /// <summary>
    /// <b>F (CI).</b> <c>POST /api/agreement-configs/{id}/publish</c> (a draft whose Monday-only fields equal its
    /// predecessor's, date omitted, so today) waits on <c>(148, 1)</c>, holds only its virtual transaction id, and
    /// answers 200 once the lock is released.
    /// <para><b>Red conditions (before TASK-14823 no configuration write takes <c>(148, 1)</c>).</b> The publish
    /// completes 200 while the lock is held, so <c>Assert.True(observed)</c> in <see cref="AssertWaitsFirstAsync"/>
    /// fails. A publish that took another lock first would fail the granted-locks assertion. The serial premise is
    /// the class's.</para>
    /// </summary>
    [Fact]
    public async Task AgreementConfigPublish_WaitsFirst_HoldsNothingElse()
    {
        var client = GlobalAdminClient(_host);
        var code = "PILPUB" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var (firstId, firstVersion) = await CreateConfigDraftAsync(client, code, 100m);
        using (var first = await SendAsync(client, HttpMethod.Post, $"/api/agreement-configs/{firstId}/publish",
                   new { appliesFrom = "2026-03-02" }, firstVersion))
            Assert.True(first.StatusCode == HttpStatusCode.OK, $"the predecessor publish: {await first.Content.ReadAsStringAsync()}");
        var (draftId, draftVersion) = await CreateConfigDraftAsync(client, code, 150m);

        await AssertWaitsFirstAsync(
            () => SendAsync(client, HttpMethod.Post, $"/api/agreement-configs/{draftId}/publish", body: null, draftVersion),
            HttpStatusCode.OK, "the agreement-config publish");
    }

    /// <summary>
    /// <b>F (CI).</b> <c>POST /api/admin/position-overrides</c> ((AC, OK24, <c>SPECIALIST</c>), <c>maxFlexBalance</c>
    /// only, date omitted) waits on the lock and answers 201 after the release.
    /// <para><b>Red conditions.</b> As above: the create completes before it is observed waiting.</para>
    /// </summary>
    [Fact]
    public async Task PositionOverrideCreate_WaitsFirst_HoldsNothingElse()
    {
        var client = GlobalAdminClient(_host);

        await AssertWaitsFirstAsync(
            () => client.PostAsJsonAsync("/api/admin/position-overrides", OverrideBody("SPECIALIST")),
            HttpStatusCode.Created, "the position-override create");
    }

    /// <summary>
    /// <b>F (CI).</b> <c>PUT /api/admin/position-overrides/{id}</c> on the seeded (AC, OK24, <c>RESEARCHER</c>)
    /// override (<c>maxFlexBalance</c> from none to 150, its real version, date omitted) waits on the lock and
    /// answers 200 after the release.
    /// <para><b>Red conditions.</b> As above.</para>
    /// </summary>
    [Fact]
    public async Task PositionOverrideUpdate_WaitsFirst_HoldsNothingElse()
    {
        var client = GlobalAdminClient(_host);
        var (overrideId, version) = await SeededResearcherOverrideAsync();

        await AssertWaitsFirstAsync(
            () => SendAsync(client, HttpMethod.Put, $"/api/admin/position-overrides/{overrideId}",
                OverrideBody("RESEARCHER"), version),
            HttpStatusCode.OK, "the position-override PUT");
    }

    /// <summary>
    /// <b>F (CI).</b> <c>POST /api/admin/position-overrides/{id}/deactivate</c> on that override (it sets no weekly
    /// norm, its real version, date omitted) waits on the lock and answers 200 after the release.
    /// <para><b>Red conditions.</b> As above.</para>
    /// </summary>
    [Fact]
    public async Task PositionOverrideDeactivate_WaitsFirst_HoldsNothingElse()
    {
        var client = GlobalAdminClient(_host);
        var (overrideId, version) = await SeededResearcherOverrideAsync();

        await AssertWaitsFirstAsync(
            () => SendAsync(client, HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/deactivate", body: null, version),
            HttpStatusCode.OK, "the position-override deactivate");
    }

    /// <summary>
    /// <b>F (CI).</b> <c>POST /api/admin/position-overrides/{id}/activate</c> on that override, deactivated beforehand
    /// (dated Monday 2 March 2026, through the route, before the test takes the lock), its real version, date omitted,
    /// waits on the lock and answers 200 after the release.
    /// <para><b>Red conditions.</b> As above.</para>
    /// </summary>
    [Fact]
    public async Task PositionOverrideActivate_WaitsFirst_HoldsNothingElse()
    {
        var client = GlobalAdminClient(_host);
        var (overrideId, version) = await SeededResearcherOverrideAsync();
        using (var deactivated = await SendAsync(client, HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/deactivate",
                   new { appliesFrom = "2026-03-02" }, version))
            Assert.True(deactivated.StatusCode == HttpStatusCode.OK, $"the set-up deactivation: {await deactivated.Content.ReadAsStringAsync()}");
        var (_, inactiveVersion) = await SeededResearcherOverrideAsync();

        await AssertWaitsFirstAsync(
            () => SendAsync(client, HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/activate", body: null, inactiveVersion),
            HttpStatusCode.OK, "the position-override activate");
    }

    /// <summary>
    /// <b>F (CI).</b> <c>PUT /api/config/STY01/profile/HK/OK24</c> (<c>If-None-Match: *</c>, dated Monday 2 March 2026,
    /// <c>maxFlexBalance</c> only) waits on the lock and answers 200 after the release.
    /// <para><b>Red conditions.</b> As above: before TASK-14823 the profile write takes no pay-inputs lock.</para>
    /// </summary>
    [Fact]
    public async Task LocalProfileWrite_WaitsFirst_HoldsNothingElse()
    {
        var client = GlobalAdminClient(_host);

        await AssertWaitsFirstAsync(
            () => PutProfileAsync(client, "STY01", ifMatchVersion: null, "2026-03-02", 30m),
            HttpStatusCode.OK, "the local-profile write");
    }

    /// <summary>
    /// <b>F (CI).</b> AC-C2u (ruling A9, Case A). A local-profile save whose predecessor is superseded while it waits
    /// for the pay-inputs lock is refused with the route's existing 412 and writes nothing. R1 of STY01's (HK, OK24)
    /// profile runs from Monday 5 January 2026 at version 1. The request (<c>If-Match: "1"</c>, dated Monday 2 March
    /// 2026, <c>maxFlexBalance</c> only) reads R1 before its transaction, then waits for the lock the test holds.
    /// Once PostgreSQL shows it waiting, the test closes R1 at Monday 2 February 2026 and inserts R2 from that date
    /// at version 1 (the request holds no row lock yet). On release the request locks R2, whose version also
    /// satisfies <c>If-Match: "1"</c>, so only the route's comparison of the locked row with the row it read can
    /// refuse it: 412, R1 closed and R2 open and no third profile row, and no audit row, outbox event or worklist
    /// row from the request (each read filtered to the request's key and actor).
    /// <para><b>Red conditions.</b> Before TASK-14823 the local-profile write takes no pay-inputs lock, so the
    /// request never waits and the "observed waiting" assertion fails, as in the row-(2) facts above. The guard's own
    /// RED is M-49 (CI-M5): with the route's identity comparison removed and the repository's version check kept, the
    /// stale <c>If-Match: "1"</c> passes against R2, the request supersedes R2 at 2 March and answers 200, so the
    /// 412 assertion, the "R2 open" assertion and the "no audit row" assertion all fail.</para>
    /// </summary>
    [Fact]
    public async Task LocalProfileWrite_PredecessorSupersededDuringTheWait_Refused412_NothingWritten()
    {
        const string org = "STY01";
        var r1 = Guid.NewGuid();
        var r2 = Guid.NewGuid();
        await ExecProfileSqlAsync(
            """
            INSERT INTO local_agreement_profiles
                (profile_id, org_id, agreement_code, ok_version, effective_from, effective_to, max_flex_balance, created_by, version)
            VALUES (@id, 'STY01', 'HK', 'OK24', @from, NULL, 20, 'seed_r1', 1)
            """, ("id", r1), ("from", new DateOnly(2026, 1, 5)));
        var client = GlobalAdminClient(_host);
        const string who = "the local-profile save with a stale predecessor";

        await using var held = await HeldLock.AcquireAsync(Cs);
        var request = PutProfileAsync(client, org, ifMatchVersion: 1, "2026-03-02", 30m);

        var observed = await PriorMonthExportRaceTests.WaitUntilAsync(
            async () => (await WaitingPidsAsync()).Count > 0, TimeSpan.FromSeconds(30), () => request.IsCompleted);
        var early = string.Empty;
        if (request.IsCompletedSuccessfully)
        {
            var answered = await request;
            early = $" The request already answered {(int)answered.StatusCode}: {await answered.Content.ReadAsStringAsync()}";
        }
        Assert.True(observed,
            $"{who} was never observed waiting on the pay-inputs lock (148, 1) (request status: {request.Status}).{early}" +
            PriorMonthExportRaceTests.DescribeFault(request));
        Assert.False(request.IsCompleted, $"{who} finished while the pay-inputs lock was still held.");
        await AssertHoldsNothingElseAsync(Assert.Single(await WaitingPidsAsync()), who);

        // The other administrator's save, committed while the request waits: R1 closed, R2 opened from the same day.
        await ExecProfileSqlAsync(
            "UPDATE local_agreement_profiles SET effective_to = @cut WHERE profile_id = @r1",
            ("r1", r1), ("cut", new DateOnly(2026, 2, 2)));
        await ExecProfileSqlAsync(
            """
            INSERT INTO local_agreement_profiles
                (profile_id, org_id, agreement_code, ok_version, effective_from, effective_to, max_flex_balance, created_by, version)
            VALUES (@r2, 'STY01', 'HK', 'OK24', @cut, NULL, 25, 'seed_r2', 1)
            """, ("r2", r2), ("cut", new DateOnly(2026, 2, 2)));

        await held.ReleaseAsync();
        using var rsp = await request;
        Assert.True(rsp.StatusCode == HttpStatusCode.PreconditionFailed,
            $"{who} should answer 412 once the lock is released, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");

        Assert.Equal(2L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM local_agreement_profiles WHERE org_id = 'STY01' AND agreement_code = 'HK' AND ok_version = 'OK24'"));
        Assert.Equal(1L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM local_agreement_profiles WHERE profile_id = @r1 AND effective_to = @cut",
            ("r1", r1), ("cut", new DateOnly(2026, 2, 2))));
        Assert.Equal(1L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM local_agreement_profiles WHERE profile_id = @r2 AND effective_to IS NULL AND version = 1 AND max_flex_balance = 25",
            ("r2", r2)));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM local_agreement_profile_audit WHERE actor_id = @a", ("a", "ADMIN_S148_ORG")));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = 'local-agreement-profile-STY01-HK-OK24'"));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM audit_projection WHERE actor_id = @a", ("a", "ADMIN_S148_ORG")));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE created_by = @a", ("a", "ADMIN_S148_ORG")));
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

    // Row (2)'s request helpers (TASK-14823-T).

    /// <summary>A draft agreement config on (<paramref name="agreementCode"/>, OK24) through the real create route; its id and ETag version.</summary>
    private static async Task<(Guid Id, long Version)> CreateConfigDraftAsync(
        HttpClient client, string agreementCode, decimal maxFlexBalance)
    {
        using var rsp = await client.PostAsJsonAsync("/api/agreement-configs", new
        {
            agreementCode,
            okVersion = "OK24",
            description = "S148 pay-inputs lock census",
            normModel = "WEEKLY_HOURS",
            weeklyNormHours = 37m,
            normPeriodWeeks = 1,
            annualNormHours = 1924m,
            maxFlexBalance,
            hasOvertime = true,
            hasMerarbejde = false,
            overtimeThreshold50 = 37m,
            overtimeThreshold100 = 44m,
            eveningSupplementEnabled = false,
            nightSupplementEnabled = false,
            weekendSupplementEnabled = false,
            holidaySupplementEnabled = false,
            eveningStart = 17,
            eveningEnd = 23,
            nightStart = 23,
            nightEnd = 6,
            eveningRate = 1.25m,
            nightRate = 1.5m,
            weekendSaturdayRate = 1.5m,
            weekendSundayRate = 2m,
            holidayRate = 2m,
            onCallDutyEnabled = false,
            onCallDutyRate = 0.33m,
            callInWorkEnabled = false,
            callInMinimumHours = 3m,
            callInRate = 1m,
            travelTimeEnabled = false,
            workingTravelRate = 1m,
            nonWorkingTravelRate = 0.5m,
            maxDailyHours = 13m,
            minimumRestHours = 11m,
            restPeriodDerogationAllowed = false,
            weeklyMaxHoursReferencePeriod = 17,
            voluntaryUnsocialHoursAllowed = true,
        });
        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Created, $"the set-up draft create: {(int)rsp.StatusCode}: {text}");
        var id = System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("configId").GetGuid();
        return (id, long.Parse(rsp.Headers.ETag!.Tag.Trim('"'), CultureInfo.InvariantCulture));
    }

    /// <summary>An override write body for (AC, OK24, <paramref name="positionCode"/>): <c>maxFlexBalance</c> 150, no weekly norm, date omitted.</summary>
    private static object OverrideBody(string positionCode) => new
    {
        agreementCode = "AC",
        okVersion = "OK24",
        positionCode,
        maxFlexBalance = 150m,
        normPeriodWeeks = 4,
        weeklyNormHours = (decimal?)null,
        description = "S148 pay-inputs lock census",
    };

    /// <summary>The seeded (AC, OK24, RESEARCHER) override's id and its CURRENT head version.</summary>
    private async Task<(Guid Id, long Version)> SeededResearcherOverrideAsync()
    {
        const string where = "WHERE agreement_code = 'AC' AND ok_version = 'OK24' AND position_code = 'RESEARCHER'";
        var id = await ScalarStringAsync(Cs, "SELECT override_id::text FROM position_override_configs " + where);
        var version = await ScalarLongAsync(Cs, "SELECT version FROM position_override_configs " + where);
        return (Guid.Parse(id!), version);
    }

    /// <summary>The local-profile PUT for (<paramref name="orgId"/>, HK, OK24) with <c>maxFlexBalance</c> only: <c>If-Match "n"</c>, or <c>If-None-Match: *</c> when null.</summary>
    private static async Task<HttpResponseMessage> PutProfileAsync(
        HttpClient client, string orgId, long? ifMatchVersion, string effectiveFrom, decimal maxFlexBalance)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/config/{orgId}/profile/HK/OK24")
        {
            Content = JsonContent.Create(new { effectiveFrom, maxFlexBalance }),
        };
        if (ifMatchVersion is { } version)
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        else
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return await client.SendAsync(req);
    }

#pragma warning disable CA2100 // each sql is a literal at its call site; values flow through parameters (QUAL-073).
    private async Task ExecProfileSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
#pragma warning restore CA2100

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
