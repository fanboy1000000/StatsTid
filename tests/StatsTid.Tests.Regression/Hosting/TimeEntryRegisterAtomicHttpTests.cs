using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S133 / TASK-13307 (QUAL-016 SPIKE) — the WIRE-DRIVEN conversion of a Pattern-C (non-audit,
/// projection-backed) atomic-outbox proof for <c>POST /api/time-entries</c>.
///
/// <para><b>What this replaces and why (plain-language):</b> the legacy
/// <see cref="StatsTid.Tests.Regression.Outbox.TimeProjectionAtomicTests"/>.<c>RegisterTimeEntry_OutboxFails_RollsBack</c>
/// exercises the repositories DIRECTLY — it opens its own tx and calls
/// <c>throwingOutbox.EnqueueAndReturnIdAsync</c> + <c>timeRepo.InsertAsync</c> in the test body. It
/// never touches the HTTP route, so it proves the REPOSITORY contract but NOT that the shipped
/// <c>POST /api/time-entries</c> handler still wraps enqueue+projection in one transaction. This
/// test drives the REAL endpoint over authenticated HTTP through a host whose
/// <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/> throws
/// (<see cref="StatsTidWebApplicationFactory.WithThrowingOutbox"/>).</para>
///
/// <para><b>Falsifiability (the guard this now pins):</b> the handler opens ONE ReadCommitted tx,
/// takes the per-employee advisory lock, runs the approval-period save-lock check, then
/// <c>EnqueueAndReturnIdAsync(conn,tx)</c> FIRST and <c>timeProjectionRepo.InsertAsync(conn,tx)</c>
/// SECOND, and only then <c>CommitAsync</c>; its <c>catch { RollbackAsync; throw }</c> surfaces the
/// fault as a 5xx. The throwing outbox faults the FIRST (enqueue) step, so the projection INSERT is
/// never even reached and the tx rolls back. This goes RED if the route is deleted (404/405), if
/// <c>CommitAsync</c> moves before the enqueue, or if the projection INSERT is switched to a
/// self-managed (own-connection) overload that would survive the rollback — none of which the
/// repo-direct legacy test can detect.</para>
///
/// <para><b>Auth/seed cost (spike measurement, amended S136):</b> the cheapest end of the family.
/// The endpoint's policy is <c>EmployeeOrAbove</c> (<c>requireOrgScope:false</c>); an Employee
/// acting on their OWN data (<c>request.EmployeeId == token sub</c>) short-circuits the org-scope
/// validator, so a bare <c>role=Employee</c> token for the same id admits with NO scope. No
/// approval-period row is seeded, so the in-lock save-lock check reads "no row ⇒ writable" and
/// proceeds to the enqueue. <b>S136 / TASK-13603 amendment:</b> the handler now RESOLVES THE
/// SUBJECT (terminated-inclusive users read, ADR-040 D3 / SEC-046) and consults the employment
/// window in-lock, so a <c>users</c> row IS a precondition (404 without one) — the original
/// "ZERO row seeding" measurement no longer holds; the test seeds the canonical
/// <see cref="StatsTid.Tests.Regression.TestSupport.RegressionSeed"/> employee (NULL employment
/// dates ⇒ window-unbounded per ADR-040 D2, so the gate passes through and the rollback proof is
/// unchanged). Because the stream id <c>employee-{employeeId}</c> is chosen by the test, the
/// outbox/event checks reuse the stream-keyed
/// <see cref="ForcedRollbackHarness.AssertNoOutboxRowAsync"/> /
/// <see cref="ForcedRollbackHarness.AssertNoEventRowAsync"/> helpers directly (unlike the Pattern-B
/// create, whose id is server-generated).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TimeEntryRegisterAtomicHttpTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot base-host seeders against the empty DB before deriving
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public async Task RegisterTimeEntry_RealEndpoint_OutboxThrows_RollsBackWholeWrite()
    {
        var employeeId = "EMP_FR_HTTP_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"employee-{employeeId}";

        // S136 / TASK-13603 — the handler now resolves the subject (terminated-inclusive users
        // read, ADR-040 D3) before opening the write tx, so the employee must exist. Seeded with
        // NULL employment dates (window-unbounded, D2) and is_active=true, so the new gates pass
        // through and this test still pins exactly what it always pinned: the atomic rollback.
        // Direct-SQL seed, no outbox involvement — safe alongside the throwing host.
        await StatsTid.Tests.Regression.TestSupport.RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, "STY02", "HK", "OK24", ensureOrg: false);

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = throwingHost.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));

        var postBody = new
        {
            employeeId,
            date = new DateOnly(2026, 5, 7).ToString("yyyy-MM-dd"),
            hours = 7.4m,
            taskId = "PROJ-QUAL016-FR",
            activityType = "NORMAL",
            agreementCode = "HK",
        };
        var rsp = await client.PostAsJsonAsync("/api/time-entries", postBody);

        // The enqueue threw before commit; the handler's catch rolls back and rethrows → 5xx.
        Assert.True((int)rsp.StatusCode >= 500,
            $"expected a 5xx from the escaped outbox throw, got {(int)rsp.StatusCode}");

        // Whole-write rollback — no outbox row, no canonical event, no projection row on a fresh
        // connection. Outbox/event checks reuse the stream-keyed helpers verbatim.
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await AssertNoProjectionRowAsync(_harness.ConnectionString, "time_entries_projection", employeeId);
    }

    /// <summary>
    /// Asserts no <c>time_entries_projection</c> row exists for the employee on a fresh connection.
    /// (Pattern-C projection tables have no dedicated helper on
    /// <see cref="ForcedRollbackHarness"/> — the legacy projection suites carry an identical local
    /// helper; the spike keeps that shape rather than widening the shared harness.)
    /// </summary>
    private static async Task AssertNoProjectionRowAsync(
        string connectionString, string tableName, string employeeId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {tableName} WHERE employee_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        Assert.Equal(0L, count);
    }

    /// <summary>
    /// A JWT signed with the dev-fallback key claiming <c>role=Employee</c> with <c>sub</c> ==
    /// <paramref name="employeeId"/>. Posting for that same id is the self-access case that
    /// bypasses org-scope validation, so no <c>orgId</c>/<c>scopes</c> claim is needed.
    /// </summary>
    private static string MintEmployeeToken(string employeeId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: employeeId,
            name: employeeId,
            role: StatsTidRoles.Employee,
            agreementCode: "HK");
    }
}
