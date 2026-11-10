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

        var saveA = Task.Run(() => SaveAbsenceAsync(clientA, emp, "VACATION"));
        var saveB = Task.Run(() => SaveAbsenceAsync(clientB, emp, "SICK_DAY"));

        // Both saves are genuinely queued behind the holder: each has passed its advisory check.
        await WaitForWaitersAsync(classId, objId, expected: 2);
        Assert.Equal(0m, await SumCurrentAbsenceHoursAsync(emp)); // nothing committed while the lock is held

        await blockerTx.CommitAsync();

        var rspA = await saveA;
        var rspB = await saveB;
        var statuses = new[] { rspA.StatusCode, rspB.StatusCode }.OrderBy(s => (int)s).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }, statuses);

        var loser = rspA.StatusCode == HttpStatusCode.UnprocessableEntity ? rspA : rspB;
        Assert.Equal("paid-absence-day-bound",
            JsonDocument.Parse(await BodyAsync(loser)).RootElement.GetProperty("kind").GetString());

        // THE invariant: the day holds no more credit-class absence than it was worth.
        Assert.Equal(7.4m, await SumCurrentAbsenceHoursAsync(emp));
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
