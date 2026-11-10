using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Backend.Api.Services;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Approval;
using StatsTid.Tests.Regression.TestSupport;
using Xunit;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14804-T — AC-S0-7e, the in-lock half of the paid-absence day bound (ADR-043 D2):
/// two saves racing for one day cannot together exceed it. Each save's advisory check runs BEFORE
/// the transaction, against a day that is still empty, so both pass it; only the check inside the
/// per-employee lock (<see cref="EmployeeConsumptionLock"/>) can see the other's row.
///
/// <para><b>Ordering is forced by observation, never by a sleep:</b> a third connection holds the
/// employee's advisory lock; the two real saves are fired and a poll of <c>pg_locks</c> blocks until
/// BOTH are observed waiting on the key (each has therefore passed its advisory check); only then
/// does the holder commit. The poll times out (fails loud) if a save never reaches the lock.</para>
///
/// <para>Docker fact: CI-pending (CI-W1).</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendConcurrency")]
public sealed class PaidAbsenceDayBoundRaceTests : SendConcurrencyTestBase
{
    private static readonly DateOnly Friday = new(2026, 6, 12);

    public PaidAbsenceDayBoundRaceTests(SendConcurrencyFixture fx) : base(fx) { }

    /// <summary>AC-S0-7e. Friday 12 June 2026 (planned 7,4, nothing worked). Save A (VACATION 7,4)
    /// and save B (SICK_DAY 7,4) are two DIFFERENT cells, so neither replaces the other. Exactly one
    /// answers 200, the other 422 with <c>kind = "paid-absence-day-bound"</c>, and the day's current
    /// credit-class hours are 7,4.
    ///
    /// <para>Kind: F (<c>R → F</c> against the plan's row, ruled B8). Red conditions: on the base code
    /// each cap sums only its own request (<c>SkemaEndpoints.cs:1044</c>, in-lock <c>:2034</c>), so both
    /// racers answer 200 and the "exactly one 422" and "7,4 stored" assertions trip.</para></summary>
    [Fact]
    public async Task TwoConcurrentSaves_NeverExceedTheBound()
    {
        var emp = UniqueEmp("7e");
        var (rspA, rspB) = await RaceAsync(emp,
            (client, e) => SaveAbsenceAsync(client, e, "VACATION"),
            (client, e) => SaveAbsenceAsync(client, e, "SICK_DAY"));

        var statuses = new[] { rspA.StatusCode, rspB.StatusCode }.OrderBy(s => (int)s).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }, statuses);

        var loser = rspA.StatusCode == HttpStatusCode.UnprocessableEntity ? rspA : rspB;
        Assert.Equal("paid-absence-day-bound",
            JsonDocument.Parse(await BodyAsync(loser)).RootElement.GetProperty("kind").GetString());

        // THE invariant: the day holds no more credit-class absence than it was worth.
        Assert.Equal(7.4m, await SumCurrentAbsenceHoursAsync(emp));
    }

    /// <summary>The save-vs-save direction: save A is an absence (<c>SICK_DAY</c> 7,4) and save B
    /// carries no absences, only work time 08:00-15:24, on Friday 12 June 2026. Exactly one answers
    /// 200 and the other 422 with the bound's <c>kind</c>; the day never holds both the sick row and
    /// the work row.
    ///
    /// <para>Kind: F. Red conditions: on the base code the absence cap sums only its own request and a
    /// work-only save skips both absence blocks (<c>SkemaEndpoints.cs:982</c>, <c>:1985</c>), so both
    /// racers answer 200 and the "exactly one 422" assertion trips (first assertion to fail: the
    /// status pair).</para></summary>
    [Fact]
    public async Task AbsenceSaveVsWorkSave_NeverExceedTheBound()
    {
        var emp = UniqueEmp("7f");
        var (rspA, rspB) = await RaceAsync(emp,
            (client, e) => SaveAbsenceAsync(client, e, "SICK_DAY"),
            (client, e) => PostSkemaWorkTimeSaveAsync(client, e, Friday, "08:00", "15:24",
                year: Friday.Year, month: Friday.Month));

        var statuses = new[] { rspA.StatusCode, rspB.StatusCode }.OrderBy(s => (int)s).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }, statuses);

        var loser = rspA.StatusCode == HttpStatusCode.UnprocessableEntity ? rspA : rspB;
        Assert.Equal("paid-absence-day-bound",
            JsonDocument.Parse(await BodyAsync(loser)).RootElement.GetProperty("kind").GetString());

        // THE invariant: never both the sick row and the work row on the day.
        var sickRows = await SumCurrentAbsenceHoursAsync(emp);
        var workRows = await CountWorkTimeRowsAsync(emp, Friday);
        Assert.True((sickRows == 7.4m && workRows == 0L) || (sickRows == 0m && workRows == 1L),
            $"the day holds sick hours {sickRows} and {workRows} work row(s)");
    }

    /// <summary>The shared choreography: seeds a fresh employee, holds the employee lock on a third
    /// connection, fires both saves, waits until BOTH are observed queued on the lock (each has then
    /// passed its advisory check against the empty day), asserts nothing committed meanwhile, then
    /// releases the lock. Ordering is by observation, never by a sleep.</summary>
    private async Task<(HttpResponseMessage A, HttpResponseMessage B)> RaceAsync(
        string emp,
        Func<HttpClient, string, Task<HttpResponseMessage>> saveA,
        Func<HttpClient, string, Task<HttpResponseMessage>> saveB)
    {
        await RegressionSeed.SeedEmployeeAsync(
            Fx.ConnectionString, emp, Org, "HK", "OK26", ensureOrg: false);
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        using var stubbedHost = Fx.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IHttpClientFactory>(new PaidAbsenceRuleStubFactory())));
        var clientA = ClientFor(stubbedHost, emp, StatsTidRoles.Employee, Org, new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY"));
        var clientB = ClientFor(stubbedHost, emp, StatsTidRoles.Employee, Org, new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY"));

        await using var blockerConn = Fx.Db.Create();
        await blockerConn.OpenAsync();
        await using var blockerTx = await blockerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(blockerConn, blockerTx, emp);

        var taskA = Task.Run(() => saveA(clientA, emp));
        var taskB = Task.Run(() => saveB(clientB, emp));

        await WaitForWaitersAsync(classId, objId, expected: 2);
        Assert.Equal(0m, await SumCurrentAbsenceHoursAsync(emp));
        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp, Friday)); // nothing committed while the lock is held

        await blockerTx.CommitAsync();
        return (await taskA, await taskB);
    }

    private static Task<HttpResponseMessage> SaveAbsenceAsync(HttpClient client, string emp, string type)
        => client.PostAsJsonAsync($"/api/skema/{emp}/save", new
        {
            year = Friday.Year,
            month = Friday.Month,
            absences = new[] { new { date = Friday.ToString("yyyy-MM-dd"), absenceType = type, hours = 7.4m } },
        });

    /// <summary>The day's current (not superseded) VACATION + SICK_DAY hours.</summary>
    private async Task<decimal> SumCurrentAbsenceHoursAsync(string emp)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(hours), 0) FROM absences_projection
            WHERE employee_id = @e AND date = @d AND superseded_by_outbox_id IS NULL
              AND absence_type IN ('VACATION', 'SICK_DAY')
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        cmd.Parameters.AddWithValue("d", Friday);
        return (decimal)(await cmd.ExecuteScalarAsync())!;
    }
}
