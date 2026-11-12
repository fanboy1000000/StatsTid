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
/// S148 / TASK-14811-T fix-up cycle 3 (QUAL-236) — the in-lock half of the child's-sick judge (rule A, "where an illness
/// starts"): two saves racing cannot together put two "1st sick day" rows on consecutive days. Each save's advisory
/// check runs BEFORE the transaction, against an empty month, so both pass it; only the judge inside the per-employee lock
/// (<see cref="EmployeeConsumptionLock"/>) can see the other's row.
///
/// <para><b>Ordering is forced by observation, never by a sleep</b> (the technique of
/// <c>PaidAbsenceDayBoundRaceTests</c>, whose ordering fix is commit <c>2f1d3a57</c>): a third connection holds the
/// employee's advisory lock; both real saves are fired and a poll of <c>pg_locks</c> blocks until BOTH are observed
/// waiting on the key (each has therefore passed its advisory check against the empty month); only then does the holder
/// commit. The poll fails loud if a save never reaches the lock.</para>
///
/// <para>Docker fact: <b>CI-pending</b>; never run locally.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendConcurrency")]
public sealed class ChildSickIllnessStartRaceTests : SendConcurrencyTestBase
{
    private const string Day1 = "CHILD_SICK_DAY";
    private static readonly DateOnly Monday = new(2026, 6, 8);
    private static readonly DateOnly Tuesday = new(2026, 6, 9);

    public ChildSickIllnessStartRaceTests(SendConcurrencyFixture fx) : base(fx) { }

    /// <summary>
    /// Save A posts <c>CHILD_SICK_DAY</c> 7,4 h on Monday 8 June 2026; save B posts <c>CHILD_SICK_DAY</c> 7,4 h on Tuesday
    /// 9 June, for an opted-in HK employee (OK26). Both pass the advisory check (each sees an empty month). Whichever the
    /// lock grants first commits (200); the other is judged inside the lock against the committed row and answers 422
    /// <c>child-sick-illness-start</c> (Tuesday directly follows a 1st sick day), and the stored state holds exactly one
    /// 1st-sick-day row.
    ///
    /// <para><b>Kind F.</b> Red conditions: on the base (<c>ba247730</c>) no rule A exists, so both saves answer 200 and
    /// two rows are stored; the "exactly one 200 and one 422" assertion trips first. Mutation M-L (the in-lock call of the
    /// judge, <c>SkemaEndpoints.cs:2586-2589</c>, deleted): the advisory check passed for both, nothing judges inside the
    /// lock, both answer 200, and the same assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task TwoConcurrentDayOneSaves_OnConsecutiveDays_OnlyOneCommits()
    {
        var emp = UniqueEmp("cs1");
        await RegressionSeed.SeedEmployeeAsync(Fx.ConnectionString, emp, Org, "HK", "OK26", ensureOrg: false);
        await using (var conn = new NpgsqlConnection(Fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO employee_entitlement_eligibility (employee_id, entitlement_type, eligible, effective_from)
                VALUES (@e, 'CHILD_SICK', TRUE, '0001-01-01')
                """, conn);
            cmd.Parameters.AddWithValue("e", emp);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }
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

        var taskA = Task.Run(() => SaveAsync(clientA, emp, Monday));
        var taskB = Task.Run(() => SaveAsync(clientB, emp, Tuesday));

        await WaitForWaitersAsync(classId, objId, expected: 2);
        Assert.Equal(0, await CountDayOneRowsAsync(emp)); // nothing committed while the lock is held

        await blockerTx.CommitAsync();
        var rspA = await taskA;
        var rspB = await taskB;

        var statuses = new[] { rspA.StatusCode, rspB.StatusCode }.OrderBy(s => (int)s).ToArray();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity }, statuses);

        var loser = rspA.StatusCode == HttpStatusCode.UnprocessableEntity ? rspA : rspB;
        var body = JsonDocument.Parse(await loser.Content.ReadAsStringAsync()).RootElement;
        Assert.Multiple(
            () => Assert.Equal("absence_type_not_eligible", body.GetProperty("error").GetString()),
            () => Assert.Equal("child-sick-illness-start", body.GetProperty("kind").GetString()),
            () => Assert.Equal(Day1, body.GetProperty("absenceType").GetString()));

        // THE invariant: one 1st sick day, not two on consecutive days.
        Assert.Equal(1, await CountDayOneRowsAsync(emp));
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string emp, DateOnly date)
        => client.PostAsJsonAsync($"/api/skema/{emp}/save", new
        {
            year = date.Year,
            month = date.Month,
            absences = new[] { new { date = date.ToString("yyyy-MM-dd"), absenceType = Day1, hours = 7.4m } },
        });

    private async Task<int> CountDayOneRowsAsync(string emp)
    {
        await using var conn = new NpgsqlConnection(Fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM absences_projection
            WHERE employee_id = @e AND absence_type = 'CHILD_SICK_DAY' AND superseded_by_outbox_id IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("e", emp);
        return (int)(long)(await cmd.ExecuteScalarAsync())!;
    }
}
