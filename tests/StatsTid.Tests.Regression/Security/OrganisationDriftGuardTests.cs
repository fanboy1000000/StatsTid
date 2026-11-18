using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S148 / TASK-14821-T (AC-C13n (1)-(7); SEC-062; the TASK-14821 SEC-062 scope adjudication, ruling 6, section 2c) - six writers refuse
/// when the person's organisation changed between the scope check and the row lock.
///
/// <para>
/// <b>In plain language.</b> Six writers (the agreement-code route, the profile PUT, the profile close, the start-date
/// PUT, the end-date PUT and a bare settlement reversal) first read the person to decide whether this administrator
/// may touch them (the person's institution against the administrator's scope), and only afterwards lock the person's
/// row. If a colleague's transfer of the same person commits in that gap, the locked row sits in another institution
/// than the one checked, and carrying the change out would change someone the administrator was never checked for.
/// Each writer now compares, under its lock, the organisation its check approved with the one it locked, and answers
/// 409 "refresh and retry" with nothing written. These facts make that gap happen on purpose, as
/// <c>TransferRaceTests</c> does for the user edit (AC-C13m), whose choreography they copy.
/// </para>
///
/// <para>
/// <b>Choreography (parts (1)-(6); part (7) holds the pay-inputs key instead, see its own summary).</b> (1) A side
/// connection locks the person's <c>users</c> row (<c>FOR UPDATE</c>) in an
/// open transaction. (2) The request is sent without being awaited. (3) The test polls <c>pg_stat_activity</c> until
/// another backend waits on a lock (<c>wait_event_type = 'Lock'</c>); it gives up when the request completes, and a
/// timeout fails the fact, so a request that never reached the row lock cannot pass. (4) The request must still be
/// pending; then the side connection sets <c>primary_org_id = 'STY02'</c> and <c>version = version + 1</c> and commits.
/// (5) The request is awaited. Parts (1)-(5) send a hand-built <c>If-Match</c> of version + 1 (the version the side
/// transaction produces, so the concurrency check passes once the request has the lock and only the organisation
/// differs); part (6) sends the settlement's real version and sequence, with no guess.
/// </para>
///
/// <para>
/// <b>Assertions (every fact).</b> 409; the body holds exactly the contract members (parts (1)-(5): the one member
/// <c>error</c>; part (6): <c>error</c> and <c>failure</c> = "OrganisationDrift"), the sentence asserted as a literal and
/// never as the production constant, so a wrong constant fails and no organisation id can ride the refusal;
/// <c>users.primary_org_id</c> "STY02" at version v+1; the person's data unchanged (<see cref="SelfChangeRefusalTests.WriteTrailAsync"/>:
/// agreement-code and profile rows, the audit tables, every event on the person's streams, the projection and worklist
/// rows, the settlement rows, the employment dates), and the part's own explicit check (the profile still live; the
/// settlement still SETTLED at its version).
/// </para>
///
/// <para>
/// <b>Fixture.</b> The clock fixed at Tuesday 10 November 2026. The actor is a LocalHR scoped to STY01 only; the person
/// is seeded in STY01 at version v. One fresh database per fact. The serial-collections premise (ruling 5.6): parts
/// (1)-(6) take no pay-inputs lock themselves and part (7) holds it only inside its own fact; each leaves no request in flight, and the Regression suite runs one
/// test collection at a time (<c>xunit.runner.json:3</c>). Docker-gated: <b>CI-pending</b>.
/// </para>
///
/// <para>
/// <b>M-45 (one compound mutation, run at CI-M4a).</b> "The organisation compare deleted at all six sites; the validator
/// sibling, the locked reads and the reversal's row lock kept." On the profile PUT it removes BOTH organisation clauses
/// (the compare against the authorised organisation and the <c>auditUser</c> clause) and keeps the two null checks, the
/// locked read and the sibling call: removing only the first would leave part (2) green through the second. Frozen list:
/// exactly these seven facts (parts (1)-(7)), each tripping its 409 assertion. With the compare gone each request writes
/// (200 or 204) on a person who now lives in STY02, so <c>Assert.Equal(Conflict, status)</c> fails; the locked reads are
/// kept, so every request still reaches and waits on its lock and the wait condition holds (the fact fails on the status,
/// not on the wait). Part (7) is red by construction when the authorised compare alone is removed, so it needs no
/// mutation of its own; the <c>auditUser</c> clause alone stays unpinned.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OrganisationDriftGuardTests : IAsyncLifetime
{
    /// <summary>The refusal's sentence, as a literal (not the production constant).</summary>
    private const string DriftError =
        "The user's organisation changed while this request was being processed; refresh and retry.";

    private const string ActorId = "hr_s148_sty01_only";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    private string Cs => _harness.ConnectionString;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
        // No leaver-flip job: the part (6) leaver is inactive with a past end date and must stay as seeded.
        _factory = new StatsTidWebApplicationFactory(Cs, withoutLeaverJob: true);
        _host = _factory.WithFixedToday(Clock);
        _ = _host.CreateClient(); // boot seeders before any fact seeds (PAT-008)
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>F (CI)</b> (part (1)). The agreement-code route, AC to HK dated today, with the person moved to STY02 while the
    /// request waits on the row lock: 409 and the exact sentence; nothing written.
    /// <para><b>Red conditions (on <c>ac47d9f1</c>, no head compares organisations under the lock).</b> The route takes the
    /// locked row for the person it was sent for and writes the new code: <c>Assert.Equal(Conflict, status)</c> fails
    /// (200). <b>M-45</b> (the compare deleted) trips the same assertion.</para>
    /// </summary>
    [Fact]
    public async Task AgreementCodeRoute_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var rsp = await RaceAsync(person, next => Build(HttpMethod.Put, $"/api/admin/users/{person}/agreement-code",
            new { agreementCode = "HK", effectiveFrom = "2026-11-10" }, next));

        await AssertDriftRefusedAsync(rsp.Response, person, rsp.VersionBefore, rsp.TrailBefore, withFailure: false);
    }

    /// <summary>
    /// <b>F (CI)</b> (part (2)). The profile PUT, fraction 1.0 to 0.8 dated today, with the person moved while the
    /// request waits: 409 and the exact sentence; nothing written.
    /// <para><b>Red conditions.</b> The PUT writes the new profile row for a person in another institution and answers
    /// 200: the status assertion fails. <b>M-45</b> trips it.</para>
    /// <para><b>Which clause refuses.</b> Here the request is blocked on the users row, after the writer's own read of
    /// the person (<c>auditUser</c>, read as STY01) and before the locked read, so either organisation clause gives its
    /// 409: the compare against the authorised organisation, or the <c>auditUser</c> clause. Part (7) isolates the first.</para>
    /// </summary>
    [Fact]
    public async Task ProfilePut_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var rsp = await RaceAsync(person, next => Build(HttpMethod.Put, $"/api/admin/employee-profiles/{person}",
            new { effectiveFrom = "2026-11-10", partTimeFraction = 0.8m, position = (string?)null, employmentCategory = (string?)null },
            next));

        await AssertDriftRefusedAsync(rsp.Response, person, rsp.VersionBefore, rsp.TrailBefore, withFailure: false);
    }

    /// <summary>
    /// <b>F (CI)</b> (part (3)). The profile close (the DELETE), with the person moved while the request waits: 409 and
    /// the exact sentence; the profile is still live (one open row, no <c>EmployeeProfileSoftDeleted</c> event).
    /// <para><b>Red conditions.</b> The close ends the profile of a person in another institution and answers 204: the
    /// status assertion fails (and the live-profile assertion would). <b>M-45</b> trips the status.</para>
    /// </summary>
    [Fact]
    public async Task ProfileClose_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var rsp = await RaceAsync(person, next => Build(HttpMethod.Delete, $"/api/admin/employee-profiles/{person}", null, next));

        await AssertDriftRefusedAsync(rsp.Response, person, rsp.VersionBefore, rsp.TrailBefore, withFailure: false);
        Assert.Equal(1L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM employee_profiles WHERE employee_id = @e AND effective_to IS NULL", ("e", person)));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'EmployeeProfileSoftDeleted'",
            ("s", $"employee-profile-{person}")));
    }

    /// <summary>
    /// <b>F (CI)</b> (part (4)). The start-date PUT to Monday 5 January 2026, with the person moved while the request
    /// waits: 409 and the exact sentence; the employment dates unchanged, no start-date event.
    /// <para><b>Red conditions.</b> The PUT sets the start date and answers 200: the status assertion fails.
    /// <b>M-45</b> trips it.</para>
    /// </summary>
    [Fact]
    public async Task StartDatePut_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var rsp = await RaceAsync(person, next => Build(HttpMethod.Put, $"/api/admin/employees/{person}/employment-start-date",
            new { employmentStartDate = "2026-01-05" }, next));

        await AssertDriftRefusedAsync(rsp.Response, person, rsp.VersionBefore, rsp.TrailBefore, withFailure: false);
    }

    /// <summary>
    /// <b>F (CI)</b> (part (5)). The end-date PUT to Thursday 31 December 2026, with the person moved while the request
    /// waits: 409 and the exact sentence; the employment dates unchanged, no end-date event.
    /// <para><b>Red conditions.</b> The PUT sets the end date and answers 200: the status assertion fails.
    /// <b>M-45</b> trips it.</para>
    /// </summary>
    [Fact]
    public async Task EndDatePut_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var rsp = await RaceAsync(person, next => Build(HttpMethod.Put, $"/api/admin/employees/{person}/employment-end-date",
            new { employmentEndDate = "2026-12-31" }, next));

        await AssertDriftRefusedAsync(rsp.Response, person, rsp.VersionBefore, rsp.TrailBefore, withFailure: false);
    }

    /// <summary>
    /// <b>F (CI)</b> (part (6)). A bare reversal of an ACTIVE zero-bucket settlement (a leaver ending Tuesday 30 June
    /// 2026, a TERMINATION settlement for ferieår 2025, sequence and version read from the row, no guessed token), with
    /// the person moved while the request waits on the users row: 409, the exact sentence AND <c>failure</c> =
    /// "OrganisationDrift" (the body holds exactly those two members); the settlement is still SETTLED at its version, no
    /// event, no audit row, no worklist row.
    /// <para><b>Red conditions.</b> The bare reversal locks no users row at the dispatch code, so it never waits: the
    /// wait assertion fails ("never blocked"), and the reversal would answer 200 and reverse the row. <b>M-45</b> (the
    /// compare deleted, the row lock kept) lets the reversal proceed after the wait and answer 200: the status assertion
    /// fails.</para>
    /// <para><b>(unproven)</b> that the TERMINATION settle drive produces a settled row at the clock of 10 November 2026
    /// (the precedent fixtures settle at 5-6 March 2026); the due predicate needs only an inactive user and an end date
    /// before today, after the go-live floor, in the same ferieår. CI settles it.</para>
    /// </summary>
    [Fact]
    public async Task BareSettlementReversal_OrganisationChangedWhileWaiting_409_NothingWritten_NoGuessedToken()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        await using (var conn = new NpgsqlConnection(Cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE users SET employment_end_date = DATE '2026-06-30', is_active = FALSE,
                                 end_date_deactivated = TRUE, updated_at = NOW()
                WHERE user_id = @id
                """, conn);
            cmd.Parameters.AddWithValue("id", person);
            await cmd.ExecuteNonQueryAsync();
        }
        var service = _host.Services.GetRequiredService<VacationSettlementService>();
        await using (var settleConn = _host.Services.GetRequiredService<DbConnectionFactory>().Create())
        {
            await settleConn.OpenAsync();
            await using var tx = await settleConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var outcome = await service.SettleAsync(person, "VACATION", 2025, "TERMINATION", settleConn, tx);
            await tx.CommitAsync();
            Assert.True(outcome.DidSettle, "the TERMINATION settle drive must produce the settled row");
        }
        var sequence = await ScalarLongAsync(Cs,
            "SELECT sequence FROM vacation_settlements WHERE employee_id = @e AND entitlement_year = 2025 AND settlement_state = 'SETTLED'", ("e", person));
        var settlementVersion = await ScalarLongAsync(Cs,
            "SELECT version FROM vacation_settlements WHERE employee_id = @e AND entitlement_year = 2025 AND settlement_state = 'SETTLED'", ("e", person));

        var rsp = await RaceAsync(person, _ => Build(HttpMethod.Post, $"/api/admin/employees/{person}/settlement-reversal",
            new { entitlementYear = 2025, expectedSettlementSequence = sequence, mode = "BARE" }, settlementVersion));

        await AssertDriftRefusedAsync(rsp.Response, person, rsp.VersionBefore, rsp.TrailBefore, withFailure: true);
        Assert.Equal("SETTLED|" + settlementVersion, await ScalarStringAsync(Cs,
            "SELECT settlement_state || '|' || version FROM vacation_settlements WHERE employee_id = @e AND entitlement_year = 2025 AND sequence = @s",
            ("e", person), ("s", (int)sequence)));
    }

    /// <summary>
    /// <b>F (CI)</b> (part (7); the adjudication's W4). The profile PUT of part (2) (same actor, person and body, fraction
    /// 1.0 to 0.8 dated today, <c>If-Match: "v+1"</c>), but the side transaction holds the pay-inputs key
    /// <c>pg_advisory_xact_lock(148, 1)</c> instead of the users row. Choreography: (1) the side transaction takes the key;
    /// (2) the PUT is sent without being awaited; (3) the test polls <c>pg_locks</c> until exactly one backend waits on
    /// <c>(148, 1)</c> and fails if the request completes first; (4) in the same side transaction the person is set to
    /// <c>primary_org_id = 'STY02'</c>, <c>version = version + 1</c>, and the transaction commits (releasing the key);
    /// (5) the PUT is awaited: 409, the exact sentence as the one member, <c>users</c> at STY02 and v+1, nothing written.
    /// <para><b>Why only the authorised compare can refuse.</b> The scope check read the person as STY01 before the
    /// request blocked. The request blocks on the key, which is the first statement of the transaction, so the writer's
    /// own read of the person (<c>auditUser</c>) and the locked read both happen after the commit and both see STY02: they
    /// agree. The <c>auditUser</c> clause therefore cannot fire; the compare of the locked row (STY02) with the
    /// organisation the scope check authorised (STY01) is the only clause that can answer 409. The <c>auditUser</c> clause
    /// alone stays unpinned in isolation (a double transfer between two adjacent reads cannot be forced deterministically); it can only
    /// add a refusal.</para>
    /// <para><b>Red conditions (on <c>ac47d9f1</c>).</b> No writer takes <c>(148, 1)</c> there, so the PUT never waits on
    /// it and completes first: the wait assertion fails ("never blocked"), and the unguarded PUT would answer 200.
    /// <b>M-45</b> (both organisation clauses removed on the profile PUT) lets the PUT proceed after the commit and answer
    /// 200: <c>Assert.True(status == Conflict)</c> fails. No timing luck: the side transaction holds the key until the
    /// wait is observed, so the request cannot finish first, and the move commits only after that.</para>
    /// <para>Serial-collections premise (ruling 5.6): the key is system-wide, so the fact is safe only because the
    /// Regression suite runs one test collection at a time (<c>xunit.runner.json:3</c>).</para>
    /// </summary>
    [Fact]
    public async Task ProfilePut_OrganisationChangedWhileWaitingForThePayInputsLock_409_NothingWritten()
    {
        var person = await SeedPersonAsync(Cs, "STY01");
        var rsp = await RaceAsync(person, next => Build(HttpMethod.Put, $"/api/admin/employee-profiles/{person}",
            new { effectiveFrom = "2026-11-10", partTimeFraction = 0.8m, position = (string?)null, employmentCategory = (string?)null },
            next), holdPayInputsLock: true);

        await AssertDriftRefusedAsync(rsp.Response, person, rsp.VersionBefore, rsp.TrailBefore, withFailure: false);
    }

    // ─── the choreography ────────────────────────────────────────────────────────

    private static HttpRequestMessage Build(HttpMethod method, string url, object? body, long ifMatchVersion)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        return req;
    }

    private sealed record Raced(HttpResponseMessage Response, long VersionBefore, string? TrailBefore);

    /// <summary>
    /// Takes the person's users row on a side connection (parts (1)-(6)), or, when <paramref name="holdPayInputsLock"/> is
    /// true, the pay-inputs key <c>(148, 1)</c> instead (part (7)); sends the request (built with version + 1); waits until a
    /// backend waits on that lock (any lock wait for the row; exactly one waiter on the key); then moves the person to STY02
    /// (version + 1) on the side connection and commits, and awaits the request. Fails when the request never reached the
    /// lock wait, or finished first.
    /// </summary>
    private async Task<Raced> RaceAsync(
        string person, Func<long, HttpRequestMessage> build, bool holdPayInputsLock = false)
    {
        var version = await ScalarLongAsync(Cs, "SELECT version FROM users WHERE user_id = @u", ("u", person));
        var trailBefore = await SelfChangeRefusalTests.WriteTrailAsync(Cs, person);
        var client = LocalHrClient(_host, ActorId, "STY01");

        await using var side = new NpgsqlConnection(Cs);
        await side.OpenAsync();
        await using var sideTx = await side.BeginTransactionAsync();
        if (holdPayInputsLock)
        {
            // Part (7): the side transaction holds the system-wide pay-inputs key, the literal (148, 1), never a production
            // constant, so a writer that does not take that key as its first statement cannot be made to wait here.
            await using var keyCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(148, 1)", side, sideTx);
            await keyCmd.ExecuteScalarAsync();
        }
        else
        {
            await using var lockCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE user_id = @u FOR UPDATE", side, sideTx);
            lockCmd.Parameters.AddWithValue("u", person);
            await lockCmd.ExecuteScalarAsync();
        }

        var requestTask = client.SendAsync(build(version + 1));

        var observed = holdPayInputsLock
            ? await WaitForPayInputsLockWaiterAsync(requestTask)
            : await WaitForLockWaiterAsync(requestTask);
        Assert.True(observed,
            "the request never blocked on a lock within the timeout (it did not reach the race window; request status: " +
            $"{requestTask.Status}).");
        Assert.False(requestTask.IsCompleted, "the request finished before the side transaction committed.");

        await using (var moveCmd = new NpgsqlCommand(
            "UPDATE users SET primary_org_id = 'STY02', version = version + 1 WHERE user_id = @u", side, sideTx))
        {
            moveCmd.Parameters.AddWithValue("u", person);
            Assert.Equal(1, await moveCmd.ExecuteNonQueryAsync());
        }
        await sideTx.CommitAsync();

        return new Raced(await requestTask, version, trailBefore);
    }

    private async Task AssertDriftRefusedAsync(
        HttpResponseMessage rsp, string person, long versionBefore, string? trailBefore, bool withFailure)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Conflict, $"expected 409, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        var members = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(withFailure ? new[] { "error", "failure" } : new[] { "error" }, members);
        Assert.Equal(DriftError, doc.RootElement.GetProperty("error").GetString());
        if (withFailure)
            Assert.Equal("OrganisationDrift", doc.RootElement.GetProperty("failure").GetString());

        Assert.Equal("STY02", await UsersPrimaryOrgAsync(Cs, person));
        Assert.Equal(versionBefore + 1,
            await ScalarLongAsync(Cs, "SELECT version FROM users WHERE user_id = @u", ("u", person)));
        Assert.Equal(trailBefore, await SelfChangeRefusalTests.WriteTrailAsync(Cs, person));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @e", ("e", person)));
    }

    /// <summary>Polls <c>pg_locks</c> until exactly one backend waits on the pay-inputs key <c>(148, 1)</c> (the two-integer
    /// advisory key space; the lock facts' waiter query, as a literal); gives up when the request completes or after 30
    /// seconds. False when it never did, so a request that never waited on that key, or two waiters, fails the fact.</summary>
    private async Task<bool> WaitForPayInputsLockWaiterAsync(Task request)
    {
        return await Payroll.PriorMonthExportRaceTests.WaitUntilAsync(async () =>
        {
            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM pg_locks WHERE locktype = 'advisory' AND classid = 148 AND objid = 1 AND objsubid = 2 AND NOT granted",
                conn);
            return await cmd.ExecuteScalarAsync() is 1L;
        }, TimeSpan.FromSeconds(30), () => request.IsCompleted);
    }

    /// <summary>Polls <c>pg_stat_activity</c> until another backend waits on a lock (<c>wait_event_type = 'Lock'</c>);
    /// gives up when the request completes or after 30 seconds. False when it never did.</summary>
    private async Task<bool> WaitForLockWaiterAsync(Task request)
    {
        return await Payroll.PriorMonthExportRaceTests.WaitUntilAsync(async () =>
        {
            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND datname = current_database()
                      AND wait_event_type = 'Lock'
                )
                """, conn);
            return await cmd.ExecuteScalarAsync() is true;
        }, TimeSpan.FromSeconds(30), () => request.IsCompleted);
    }
}
