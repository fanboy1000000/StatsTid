using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S148 / TASK-14802-T (AC-S0-1a; QUAL-232, QUAL-202; R-20) — HOST-LEVEL pin that the old time-registration route,
/// <c>POST /api/time-entries</c>, no longer exists, while its read, <c>GET /api/time-entries/{employeeId}</c>, still does.
///
/// <para>
/// <b>In plain language.</b> That route let a caller register hours typed as on-call, call-in or travel, which payroll then
/// paid wrongly. No screen used it, so it was deleted; the Skema save is the one way left to write time. This fact proves
/// the door is shut (the employee's well-formed request gets "not found" or "method not allowed" and nothing is stored) and
/// that the read beside it, which the weekly calculation and the screens use, is untouched.
/// </para>
///
/// <para>
/// The precedent for a retired route's pin is <c>Payroll/RawExportRoutesRetiredTests</c>. The body below is the shape the
/// route used to accept, so on the code before this task the route would have bound it and answered 201.
/// </para>
///
/// Docker-gated (Testcontainers Postgres + full init.sql): CI-pending until the sprint checkpoint, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class TimeEntryRouteRetiredTests : IAsyncLifetime
{
    private const string Org = "S148RT01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// AC-S0-1a (kind F, Docker; CI-pending). An employee posting a well-formed body for themselves to
    /// <c>POST /api/time-entries</c> gets <b>404 or 405</b>, and no <c>time_entries_projection</c> row and no
    /// <c>TimeEntryRegistered</c> outbox row exists afterwards. The same employee's
    /// <c>GET /api/time-entries/{employeeId}</c> answers <b>200</b> with a bare array.
    ///
    /// <para><b>Red conditions.</b> On the base (the parent of TASK-14802) the route is mapped: the employee's own, valid
    /// request answers <b>201</b> and writes a projection row and an outbox event, so the status assertion fails first (and
    /// the zero-row and zero-event assertions would fail next). Re-adding the route fails it again. The GET half holds on the
    /// base and must keep holding: it is what stops a "delete every time-entries route" change from passing.</para>
    /// </summary>
    [Fact]
    public async Task Post_Answers404Or405_GetStill200()
    {
        var emp = "s148rt_emp1";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, emp, Org, "AC", "OK24");
        using var employee = S120ContractAssert.EmployeeClient(_factory, emp, Org);

        // The shape the route used to take: the employee's own, well-formed, ordinary-work entry on a weekday.
        using var post = await employee.PostAsJsonAsync("/api/time-entries", new
        {
            employeeId = emp,
            date = "2026-03-10",
            hours = 7.4m,
            taskId = (string?)null,
            activityType = "NORMAL",
            agreementCode = "AC",
        });

        Assert.True(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"expected 404 or 405 for the retired route, got {(int)post.StatusCode}: {await post.Content.ReadAsStringAsync()}");

        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", emp)));
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id=@s AND event_type='TimeEntryRegistered'",
            ("s", $"employee-{emp}")));

        // The read stays (the Orchestrator's weekly pipeline and the screens read it).
        using var get = await employee.GetAsync($"/api/time-entries/{emp}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind); // the bare-array contract
    }

#pragma warning disable CA2100 // every sql here is a hardcoded literal; values flow through parameters.
    private async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
#pragma warning restore CA2100
}
