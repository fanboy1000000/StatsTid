using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Organisation;

/// <summary>
/// S148 / TASK-14820-T fix-up 1 (AC-C13m; Step 5a R1, SEC-R1) — the user edit refuses when the person's
/// organisation changed between the permission check and the row lock.
///
/// <para>
/// <b>In plain language.</b> When an administrator edits a person, the endpoint first reads the person
/// to decide whether this administrator may touch them (the person's institution against the
/// administrator's scope), and only afterwards locks the person's row. If a colleague's transfer of the
/// same person is saved in that gap, the locked row sits in another institution than the one checked.
/// Carrying the edit out would change someone the administrator was never checked for. The endpoint now
/// answers 409 "refresh and retry" and writes nothing. These facts make that gap happen on purpose.
/// </para>
///
/// <para>
/// <b>Choreography (both facts).</b> (1) A side connection locks the person's <c>users</c> row
/// (<c>FOR UPDATE</c>) and keeps its transaction open. (2) The PUT is sent without awaiting it, with a
/// hand-built <c>If-Match</c> of version + 1 (the version the side transaction is about to produce): the
/// endpoint reads the person (organisation STY01), passes the scope check, and blocks on its own
/// <c>FOR UPDATE</c>. (3) The test polls <c>pg_stat_activity</c> until another backend is waiting on a
/// lock with a <c>users … FOR UPDATE</c> statement (the S105 waiter-barrier shape); a timeout fails the
/// fact, so a PUT that never reached the lock cannot pass. (4) The PUT must still be pending (so it
/// cannot have finished before the side transaction), then the side connection moves the person to
/// STY02, bumps the version and commits. (5) The PUT is awaited. The pre-read said STY01 and the locked
/// row says STY02: the guard fires.
/// </para>
///
/// <para>
/// <b>Fixture.</b> Fixed clock Tuesday 10 November 2026. The actor is a LocalHR scoped to STY01 only
/// (<c>hr_s148_sty01_only</c>); the person is a <see cref="TestSupport.RegressionSeed"/> user in STY01.
/// Docker-gated, CI-pending (F (CI)).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TransferRaceTests : IAsyncLifetime
{
    private const string DriftError =
        "The user's organisation changed while this request was being processed; refresh and retry.";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _host = _factory.WithFixedToday(Clock);
        _ = _host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>F (CI).</b> The request carries STY01 (the organisation the pre-read shows) while a concurrent
    /// transfer commits STY02 before the endpoint's row lock: 409 with the exact drift error; the live
    /// column stays STY02 at version + 1; display name, organisation rows, audit rows, the user's event
    /// stream and the worklist are untouched.
    /// <para><b>Red conditions (on <c>f02bd8f9</c>, without the guard).</b> The locked row (STY02)
    /// differs from the request (STY01), so the endpoint takes it for a transfer STY02 → STY01 and
    /// answers 200: <c>Assert.Equal(Conflict, status)</c> fails (and the row, audit, event and
    /// live-column assertions after it would too).</para>
    /// <para><b>Mutation M-44</b> (delete the guard in <c>AdminEndpoints.cs</c>): trips the status
    /// assertion first.</para>
    /// </summary>
    [Fact]
    public async Task OrganisationChangedUnderTheLock_RequestCarriesOldOrganisation_409_NothingWritten()
    {
        await RunRaceAsync(new { primaryOrgId = "STY01" });
    }

    /// <summary>
    /// <b>F (CI).</b> A name-only edit (<c>displayName</c>, no organisation) meets the same race: 409
    /// with the exact drift error and nothing written, so a rename cannot reach a person the actor was
    /// never scope-checked for.
    /// <para><b>Red conditions (on <c>f02bd8f9</c>).</b> No transfer is involved, so the endpoint renames
    /// the person now living in STY02 and answers 200: <c>Assert.Equal(Conflict, status)</c> fails (and
    /// the display-name assertion would).</para>
    /// <para><b>Mutation M-44</b>: trips the status assertion first.</para>
    /// </summary>
    [Fact]
    public async Task OrganisationChangedUnderTheLock_NameOnlyEdit_409_NothingWritten()
    {
        await RunRaceAsync(new { displayName = "S148 Renamed In The Race" });
    }

    private async Task RunRaceAsync(object body)
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01");
        var version = await ScalarLongAsync(cs, "SELECT version FROM users WHERE user_id = @u", ("u", userId));
        var nameBefore = await ScalarStringAsync(cs, "SELECT display_name FROM users WHERE user_id = @u", ("u", userId));
        var rowsBefore = await ReadOrgRowsAsync(cs, userId);
        var orgAuditBefore = await ReadOrgAuditAsync(cs, userId);
        var usersAuditBefore = await ScalarLongAsync(cs, "SELECT COUNT(*) FROM users_audit WHERE user_id = @u", ("u", userId));
        var streamBefore = await ScalarLongAsync(cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", ("s", $"user-{userId}"));
        var client = LocalHrClient(_host, "hr_s148_sty01_only", "STY01");

        await using var side = new NpgsqlConnection(cs);
        await side.OpenAsync();
        await using var sideTx = await side.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT 1 FROM users WHERE user_id = @u FOR UPDATE", side, sideTx))
        {
            lockCmd.Parameters.AddWithValue("u", userId);
            await lockCmd.ExecuteScalarAsync();
        }

        // Next version: the one the side transaction produces, so the endpoint's If-Match check passes
        // once it gets the lock and only the organisation differs.
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{version + 1}\"");
        var putTask = client.SendAsync(req);

        Assert.True(await WaitForUserRowWaiterAsync(cs),
            "the PUT never blocked on the users row lock within the timeout (it did not reach the race window).");
        Assert.False(putTask.IsCompleted, "the PUT finished before the side transaction committed.");

        await using (var moveCmd = new NpgsqlCommand(
            "UPDATE users SET primary_org_id = 'STY02', version = version + 1 WHERE user_id = @u", side, sideTx))
        {
            moveCmd.Parameters.AddWithValue("u", userId);
            Assert.Equal(1, await moveCmd.ExecuteNonQueryAsync());
        }
        await sideTx.CommitAsync();

        var rsp = await putTask;

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var json = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DriftError, json.GetProperty("error").GetString());

        Assert.Equal("STY02", await UsersPrimaryOrgAsync(cs, userId));
        Assert.Equal(version + 1, await ScalarLongAsync(cs, "SELECT version FROM users WHERE user_id = @u", ("u", userId)));
        Assert.Equal(nameBefore, await ScalarStringAsync(cs, "SELECT display_name FROM users WHERE user_id = @u", ("u", userId)));
        Assert.Equal(rowsBefore, await ReadOrgRowsAsync(cs, userId));
        Assert.Equal(orgAuditBefore, await ReadOrgAuditAsync(cs, userId));
        Assert.Equal(usersAuditBefore,
            await ScalarLongAsync(cs, "SELECT COUNT(*) FROM users_audit WHERE user_id = @u", ("u", userId)));
        Assert.Equal(streamBefore,
            await ScalarLongAsync(cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", ("s", $"user-{userId}")));
        Assert.Equal(0L,
            await ScalarLongAsync(cs, "SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @e", ("e", userId)));
    }

    /// <summary>Polls <c>pg_stat_activity</c> until another backend waits on a lock (<c>wait_event_type =
    /// 'Lock'</c>) in a <c>users … FOR UPDATE</c> statement: the PUT reached and blocked on its row
    /// lock. False on timeout (the caller fails the fact). Mirrors the S105 waiter barrier.</summary>
    private static async Task<bool> WaitForUserRowWaiterAsync(string cs, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        while (DateTime.UtcNow < deadline)
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND datname = current_database()
                      AND wait_event_type = 'Lock'
                      AND query ILIKE '%FROM users%FOR UPDATE%'
                )
                """, conn);
            if (await cmd.ExecuteScalarAsync() is true)
                return true;
            await Task.Delay(50);
        }
        return false;
    }
}
