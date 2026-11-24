using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Organisation;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14834-T (AC-C10d, plan-added; T-BC-1, ruling 11) — every refusal of the customary-days routes
/// writes nothing.
///
/// <para>
/// <b>What this suite is about, in plain language.</b> A bad value is a 400 that names its field and a code; a
/// second withdrawal, or reinstating a custom that is still in force, is a 409; and the same day cannot be in
/// force twice for the same agreement scope, even when two requests race (the second waits for the
/// institution's row, then sees the first and is refused with a 409 naming the row in the way). In every case
/// no row, audit row or event is written.
/// </para>
///
/// <para>
/// <b>Fixture.</b> The host and clock of <see cref="CustomaryDaysTests"/> (Tuesday 10 November 2026, STY01, a
/// LocalAdmin <c>LADM_S148_C10</c>). The container is shared by the class, so each fact uses a day no other
/// fact uses, and every "nothing written" reading is a before-and-after difference across the one request,
/// filtered to STY01's rows and stream. Docker is unavailable on the authoring machine: CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class CustomaryDaysValidationTests : IAsyncLifetime
{
    private const string BaseUrl = "/api/config/STY01/customary-days";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    private string Cs => _harness.ConnectionString;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _host = _factory.WithFixedToday(TransferWriteTests.Clock);
        _ = _host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private HttpClient Admin() => CustomaryDaysTests.LocalAdminClient(_host, CustomaryDaysTests.Actor, "STY01");

    private static object ClosureDay(int month, int day, string validFrom, string? agreementCode = null) => new
    {
        recurringMonth = month, recurringDay = day, kind = "CLOSURE_DAY", part = "WHOLE_DAY",
        validFrom, agreementCode,
    };

    private static async Task<Guid> IdOfAsync(HttpResponseMessage rsp)
        => (await rsp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customId").GetGuid();

    private Task<string> Snapshot(params Guid[] ids) => CustomaryDaysTests.SnapshotAsync(Cs, "STY01", ids);

    // ═════════════════════════════════════════════════════════════════════
    // 400: one case per rule family
    // ═════════════════════════════════════════════════════════════════════

    public static TheoryData<string, string, string> InvalidCases => new()
    {
        { "both day forms", "SingleDate", "DAY_FORM_INVALID" },
        { "not a calendar day", "RecurringDay", "NOT_A_CALENDAR_DAY" },
        { "kind outside the vocabulary", "Kind", "UNKNOWN_VALUE" },
        { "from-time without a time", "PartFromTime", "REQUIRED" },
        { "time not a whole minute", "PartFromTime", "NOT_WHOLE_MINUTE" },
        { "hours beyond the column", "PartHours", "EXCEEDS_STORAGE_LIMIT" },
        { "closure day with a mode", "PaidMode", "NOT_ALLOWED" },
        { "no valid-from", "ValidFrom", "REQUIRED" },
    };

    private static object InvalidBody(string name) => name switch
    {
        "both day forms" => new { recurringMonth = 6, recurringDay = 5, singleDate = "2026-06-05", kind = "CLOSURE_DAY", part = "WHOLE_DAY", validFrom = "2026-01-05" },
        "not a calendar day" => new { recurringMonth = 2, recurringDay = 30, kind = "CLOSURE_DAY", part = "WHOLE_DAY", validFrom = "2026-01-05" },
        "kind outside the vocabulary" => new { recurringMonth = 6, recurringDay = 5, kind = "paid_day_off", part = "WHOLE_DAY", validFrom = "2026-01-05" },
        "from-time without a time" => new { recurringMonth = 6, recurringDay = 5, kind = "CLOSURE_DAY", part = "FROM_TIME", validFrom = "2026-01-05" },
        "time not a whole minute" => new { recurringMonth = 6, recurringDay = 5, kind = "CLOSURE_DAY", part = "FROM_TIME", partFromTime = "12:00:30", validFrom = "2026-01-05" },
        "hours beyond the column" => new { recurringMonth = 6, recurringDay = 5, kind = "CLOSURE_DAY", part = "HOURS", partHours = 100, validFrom = "2026-01-05" },
        "closure day with a mode" => new { recurringMonth = 6, recurringDay = 5, kind = "CLOSURE_DAY", part = "WHOLE_DAY", paidMode = "GIVEN_TO_ALL", validFrom = "2026-01-05" },
        "no valid-from" => new { recurringMonth = 6, recurringDay = 5, kind = "CLOSURE_DAY", part = "WHOLE_DAY" },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>
    /// <b>F (CI), AC-C10d.</b> One create per rule family answers 400 with exactly one entry in <c>fields</c>,
    /// the expected field and code, and writes nothing.
    /// <para><b>Product code between the readings:</b> the route's pure value check, which runs before the
    /// transaction opens.</para>
    /// <para><b>Red conditions (F).</b> Before TASK-14834 the route does not exist and answers 404, so
    /// <c>Assert.Equal(HttpStatusCode.BadRequest, …)</c> fails first.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task InvalidValue_400_NothingWritten(string name, string field, string code)
    {
        var before = await Snapshot();

        var rsp = await Admin().PostAsJsonAsync(BaseUrl, InvalidBody(name));

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Customary day validation failed", body.GetProperty("error").GetString());
        var entry = Assert.Single(body.GetProperty("fields").EnumerateArray());
        Assert.Equal(field, entry.GetProperty("field").GetString());
        Assert.Equal(code, entry.GetProperty("code").GetString());
        Assert.Equal(before, await Snapshot());
    }

    // ═════════════════════════════════════════════════════════════════════
    // 409: state
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI), AC-C10d.</b> A second withdrawal of a withdrawn row (17 March) answers 409 "Customary day is
    /// already withdrawn"; the row's version and end date, the audit rows and the events are unchanged.
    /// <para><b>Product code between the readings:</b> the withdraw route's locked row read and state check.</para>
    /// <para><b>Red conditions (F).</b> No route before TASK-14834: the first create answers 404 and
    /// <c>Assert.Equal(HttpStatusCode.Created, …)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task WithdrawTwice_409_NothingWritten()
    {
        var client = Admin();
        var c = await IdOfAsync(Assert201(await client.PostAsJsonAsync(BaseUrl, ClosureDay(3, 17, "2026-01-05"))));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{BaseUrl}/{c}/withdraw", new { validTo = "2026-11-02" })).StatusCode);
        var before = await Snapshot(c);

        var rsp = await client.PostAsJsonAsync($"{BaseUrl}/{c}/withdraw", new { validTo = "2026-11-03" });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        await CustomaryDaysTests.AssertBodyAsync(rsp, ("error", "Customary day is already withdrawn"));
        Assert.Equal(before, await Snapshot(c));
        Assert.Equal(new DateOnly(2026, 11, 2), (await CustomaryDaysTests.ReadRowAsync(Cs, c)).ValidTo);
    }

    /// <summary>
    /// <b>F (CI), AC-C10d.</b> Reinstating a row that is not withdrawn (22 April) answers 409 "Customary day is
    /// not withdrawn" and writes nothing.
    /// <para><b>Product code between the readings:</b> the reinstate route's locked row read and state check.</para>
    /// <para><b>Red conditions (F).</b> No route before TASK-14834: the create answers 404, so the 201
    /// assertion fails first.</para>
    /// </summary>
    [Fact]
    public async Task ReinstateOpenRow_409_NothingWritten()
    {
        var client = Admin();
        var c = await IdOfAsync(Assert201(await client.PostAsJsonAsync(BaseUrl, ClosureDay(4, 22, "2026-01-05"))));
        var before = await Snapshot(c);

        var rsp = await client.PostAsJsonAsync($"{BaseUrl}/{c}/reinstate", new { validFrom = "2026-11-10" });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        await CustomaryDaysTests.AssertBodyAsync(rsp, ("error", "Customary day is not withdrawn"));
        Assert.Equal(before, await Snapshot(c));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 409: overlap
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI), AC-C10d.</b> C1 (14 July, from Monday 5 January 2026) is withdrawn from Monday 2 November and
    /// reinstated from Tuesday 10 November (201, a new row C2). Reinstating C1 again from Monday 16 November
    /// answers 409 with exactly the overlap body naming C2 (open, same day, same scope) and writes nothing.
    /// <para><b>Product code between the readings:</b> the second reinstate's overlap read under the
    /// organisation's lock, which sees the first reinstate's row.</para>
    /// <para><b>Red conditions (F).</b> No route before TASK-14834: the create answers 404, so the 201
    /// assertion fails first.</para>
    /// </summary>
    [Fact]
    public async Task ReinstateSameStoppedRowTwice_SecondIs409Overlap()
    {
        var client = Admin();
        var c1 = await IdOfAsync(Assert201(await client.PostAsJsonAsync(BaseUrl, ClosureDay(7, 14, "2026-01-05"))));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{BaseUrl}/{c1}/withdraw", new { validTo = "2026-11-02" })).StatusCode);
        var c2 = await IdOfAsync(Assert201(await client.PostAsJsonAsync($"{BaseUrl}/{c1}/reinstate", new { validFrom = "2026-11-10" })));
        var before = await Snapshot(c1, c2);

        var rsp = await client.PostAsJsonAsync($"{BaseUrl}/{c1}/reinstate", new { validFrom = "2026-11-16" });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        await CustomaryDaysTests.AssertBodyAsync(rsp,
            ("error", "Customary day overlaps an existing one"), ("conflictingCustomId", c2.ToString()));
        Assert.Equal(before, await Snapshot(c1, c2));
    }

    /// <summary>
    /// <b>F (CI), AC-C10d.</b> With an open recurring row (9 September, no agreement scope, from Monday
    /// 5 January 2026), a create of the same day and scope from Monday 2 March 2026 answers 409 naming that row
    /// and writes nothing; the same day for agreement scope HK answers 201.
    /// <para><b>Product code between the readings:</b> the create route's overlap read, whose match includes
    /// the agreement scope (null equals null only).</para>
    /// <para><b>Red conditions (F).</b> No route before TASK-14834: the first create answers 404, so the 201
    /// assertion fails first.</para>
    /// </summary>
    [Fact]
    public async Task CreateOverlappingOpenRow_409_OtherScopeIs201()
    {
        var client = Admin();
        var first = await IdOfAsync(Assert201(await client.PostAsJsonAsync(BaseUrl, ClosureDay(9, 9, "2026-01-05"))));
        var before = await Snapshot(first);

        var conflict = await client.PostAsJsonAsync(BaseUrl, ClosureDay(9, 9, "2026-03-02"));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await CustomaryDaysTests.AssertBodyAsync(conflict,
            ("error", "Customary day overlaps an existing one"), ("conflictingCustomId", first.ToString()));
        Assert.Equal(before, await Snapshot(first));

        var other = await client.PostAsJsonAsync(BaseUrl, ClosureDay(9, 9, "2026-03-02", agreementCode: "HK"));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.NotEqual(first, await IdOfAsync(other));
    }

    /// <summary>
    /// <b>F (CI), AC-C10d, the lock arm</b> (the AC-C13o pattern). A second connection holds STY01's organisation
    /// row (<c>FOR NO KEY UPDATE</c>); a create of 24 December from Monday 2 March 2026 is sent and the test waits
    /// until that request waits on a lock; the second connection then inserts an open row of the same day and
    /// scope and commits. The create answers 409 naming the inserted row and writes nothing.
    /// <para><b>Product code between the readings:</b> the create's acquisition of the organisation lock and its
    /// overlap read AFTER the wait.</para>
    /// <para><b>Red conditions (F).</b> Before TASK-14834 the route does not exist: the request never waits on a
    /// lock and answers 404, so the wait check fails. On a create that read before the lock, or never locked the
    /// organisation, the answer would be 201 and two open rows would remain.</para>
    /// </summary>
    [Fact]
    public async Task CreateWaitsOnTheOrganisationRow_ThenOverlap409()
    {
        var client = Admin();
        var auditBefore = await CustomaryDaysTests.ScalarLongAsync(Cs, "SELECT COUNT(*) FROM customary_days_audit WHERE org_id = 'STY01'");
        var eventsBefore = await CustomaryDaysTests.ScalarLongAsync(Cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = 'customary-days-STY01'");

        await using var side = new NpgsqlConnection(Cs);
        await side.OpenAsync();
        await using var sideTx = await side.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand("SELECT 1 FROM organizations WHERE org_id = 'STY01' FOR NO KEY UPDATE", side, sideTx))
            await lockCmd.ExecuteScalarAsync();

        var requestTask = client.PostAsJsonAsync(BaseUrl, ClosureDay(12, 24, "2026-03-02"));

        var observed = await PriorMonthExportRaceTests.WaitUntilAsync(async () =>
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
                      AND query ILIKE '%FROM organizations%FOR NO KEY UPDATE%'
                )
                """, conn);
            return await cmd.ExecuteScalarAsync() is true;
        }, TimeSpan.FromSeconds(30), () => requestTask.IsCompleted);

        if (!observed || requestTask.IsCompleted)
        {
            await sideTx.RollbackAsync();
            var finished = await Task.WhenAny(requestTask, Task.Delay(TimeSpan.FromSeconds(30))) == requestTask;
            var status = finished ? ((int)(await requestTask).StatusCode).ToString() : "still pending";
            Assert.Fail($"the create never waited on STY01's organisation row (request: {status}).");
        }

        var inserted = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO customary_days (custom_id, org_id, recurring_month, recurring_day, kind, part, valid_from, created_by)
            VALUES (@c, 'STY01', 12, 24, 'CLOSURE_DAY', 'WHOLE_DAY', DATE '2026-01-05', 'RACING_WRITER')
            """, side, sideTx))
        {
            insert.Parameters.AddWithValue("c", inserted);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await sideTx.CommitAsync();

        var rsp = await requestTask;

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        await CustomaryDaysTests.AssertBodyAsync(rsp,
            ("error", "Customary day overlaps an existing one"), ("conflictingCustomId", inserted.ToString()));
        Assert.Equal(1L, await CustomaryDaysTests.ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM customary_days WHERE org_id = 'STY01' AND recurring_month = 12 AND recurring_day = 24"));
        Assert.Equal(auditBefore, await CustomaryDaysTests.ScalarLongAsync(Cs, "SELECT COUNT(*) FROM customary_days_audit WHERE org_id = 'STY01'"));
        Assert.Equal(eventsBefore, await CustomaryDaysTests.ScalarLongAsync(Cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = 'customary-days-STY01'"));
    }

    private static HttpResponseMessage Assert201(HttpResponseMessage rsp)
    {
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        return rsp;
    }
}
