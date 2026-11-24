using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Organisation;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14834 stack FU-2 (ruling R2 (a), (b); consult § 2) — who may, and may not, reach an
/// institution's customary days, on all four routes.
///
/// <para>
/// <b>What this suite is about, in plain language.</b> The four routes (list, create, withdraw,
/// reinstate) are for the institution's own administrators. Everyone else is refused with a 403 and
/// nothing is written: an administrator of another institution, one whose administrator scope is
/// elsewhere and who is only a leader here, an HR user, an employee, a leader, a token with no scopes,
/// the administrator of the PARENT organisation. A central administrator who names an institution that
/// does not exist, or one that has been soft-deleted, is refused too, and so is that institution's own
/// administrator. A row can be reached only through its own institution's address: through another
/// institution's address it answers 404 and the list shows none of it.
/// </para>
///
/// <para>
/// <b>Fixture.</b> One Backend host with the clock fixed at Tuesday 10 November 2026 (PAT-008), shared by
/// every case of this class (<see cref="IClassFixture{TFixture}"/>), so the (actor, route) theory does not
/// start a container per case. Every case here refuses, so sharing is safe; the two cases that DO write
/// legitimately use rows of their own. The seeded rows are written by SQL: per institution one OPEN row
/// (the withdraw target) and one WITHDRAWN row (the reinstate target), so a missing check would visibly
/// succeed (a withdraw that stops the row, a reinstate that inserts a row). STYDEL is soft-deleted after
/// its rows are seeded. Docker is unavailable on the authoring machine: CI-pending.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class CustomaryDaysAccessTests : IClassFixture<CustomaryDaysAccessTests.AccessHost>
{
    private const string Denied = "Access denied";
    private const string NotCovered = "Actor scope does not cover target organization";
    private const string NoOrganisation = "Organization not found";

    private readonly AccessHost _fx;

    public CustomaryDaysAccessTests(AccessHost fx) => _fx = fx;

    private string Cs => _fx.Harness.ConnectionString;

    // (actor, route, status, reason). Reason null = a policy 403 (no body); a reason = the house 403 body.
    public static TheoryData<string, string, string, int, string?> Arms()
    {
        var data = new TheoryData<string, string, string, int, string?>();
        void Add(string actor, string org, int status, string? reason)
        {
            foreach (var route in new[] { "GET", "CREATE", "WITHDRAW", "REINSTATE" })
                data.Add(actor, route, org, status, reason);
        }
        Add("localAdminOfOtherOrganisation", "STY01", 403, NotCovered);
        Add("mixedRole", "STY01", 403, NotCovered);
        Add("localHr", "STY01", 403, null);
        Add("employee", "STY01", 403, null);
        Add("localLeaderPrimary", "STY01", 403, null);
        Add("localAdminWithoutScopes", "STY01", 403, null);
        Add("parentLocalAdmin", "STY01", 403, NotCovered);
        Add("globalAdmin", "STY99", 403, NoOrganisation);
        Add("globalAdmin", "STYDEL", 403, NoOrganisation);
        Add("deletedOrganisationLocalAdmin", "STYDEL", 403, NoOrganisation);
        return data;
    }

    /// <summary>
    /// <b>F (CI), AC-C10b (R2 (a)).</b> Each (actor, route) pair is refused with the exact status and body
    /// above, and a before-and-after snapshot of every organisation touched (rows, audit rows, events, and
    /// the seeded rows' version and end date) is unchanged. On STY99 (unknown) the withdraw and reinstate
    /// carry a random id: the answer is 403, not 404, so the organisation check precedes the row lookup. On
    /// STY01 and STYDEL they carry the seeded open (withdraw) and withdrawn (reinstate) rows.
    /// <para><b>Product code between the readings:</b> the route policy and the organisation scope check at
    /// the LocalAdmin floor, which run before any lookup or lock.</para>
    /// <para><b>Red conditions (F).</b> A check without the per-scope floor admits the mixed-role actor (its
    /// LocalLeader scope covers STY01) and the write answers 200/201, so <c>Assert.Equal(403, …)</c> fails. A
    /// route with no organisation check (or one after the row lookup) lets the other-organisation actor
    /// through, or answers 404 on STY99. A check that did not refuse a soft-deleted organisation lets the
    /// STYDEL pairs through.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Arms))]
    public async Task ActorAndRoute_Refused_NothingWritten(string actor, string route, string org, int status, string? reason)
    {
        var client = _fx.Client(actor);
        var before = await _fx.SnapshotAllAsync();

        var (openId, withdrawnId) = org switch
        {
            "STY01" => (_fx.Open01, _fx.Withdrawn01),
            "STYDEL" => (_fx.OpenDel, _fx.WithdrawnDel),
            _ => (Guid.NewGuid(), Guid.NewGuid()),
        };
        var baseUrl = $"/api/config/{org}/customary-days";
        var rsp = route switch
        {
            "GET" => await client.GetAsync(baseUrl),
            "CREATE" => await client.PostAsJsonAsync(baseUrl, new
            {
                singleDate = "2026-12-24", kind = "CLOSURE_DAY", part = "WHOLE_DAY", validFrom = "2026-01-05",
            }),
            "WITHDRAW" => await client.PostAsJsonAsync($"{baseUrl}/{openId}/withdraw", new { validTo = "2026-11-02" }),
            "REINSTATE" => await client.PostAsJsonAsync($"{baseUrl}/{withdrawnId}/reinstate", new { validFrom = "2026-11-10" }),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        Assert.Equal(status, (int)rsp.StatusCode);
        if (reason is null)
            Assert.Equal(string.Empty, await rsp.Content.ReadAsStringAsync());
        else
            await CustomaryDaysTests.AssertBodyAsync(rsp, ("error", Denied), ("reason", reason));
        Assert.Equal(before, await _fx.SnapshotAllAsync());
    }

    /// <summary>
    /// <b>F (CI), AC-C10b (R2 (b), consult row 6).</b> A row of STY01 is withdrawn legitimately by STY01's
    /// LocalAdmin (200), so it can be reinstated. STY02's LocalAdmin then reinstates it through STY02's own
    /// address: 404 "Customary day not found", and nothing is written (no new row, audit row or event, the
    /// withdrawn row unchanged).
    /// <para><b>Product code between the readings:</b> the reinstate route's organisation-bound, locked row
    /// read.</para>
    /// <para><b>Red conditions (F).</b> A row lookup that ignores the organisation finds the withdrawn row and
    /// answers 201 with a new STY02 row (a withdrawn row that was never withdrawn would answer 409 and hide
    /// the defect, which is why the row is withdrawn first), so <c>Assert.Equal(NotFound, …)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task ReinstateThroughTheWrongAddress_404_NothingWritten()
    {
        var owner = CustomaryDaysTests.LocalAdminClient(_fx.Host, "LADM_S148_ACC_OWN", "STY01");
        var created = await owner.PostAsJsonAsync("/api/config/STY01/customary-days", new
        {
            singleDate = "2026-06-12", kind = "CLOSURE_DAY", part = "WHOLE_DAY", validFrom = "2026-01-05",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customId").GetGuid();
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PostAsJsonAsync($"/api/config/STY01/customary-days/{id}/withdraw", new { validTo = "2026-11-02" })).StatusCode);
        var before = await _fx.SnapshotAllAsync(id);

        var rsp = await _fx.Client("localAdminOfOtherOrganisation")
            .PostAsJsonAsync($"/api/config/STY02/customary-days/{id}/reinstate", new { validFrom = "2026-11-10" });

        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
        await CustomaryDaysTests.AssertBodyAsync(rsp, ("error", "Customary day not found"));
        Assert.Equal(before, await _fx.SnapshotAllAsync(id));
    }

    /// <summary>
    /// <b>F (CI), AC-C10b (R2 (b), consult row 7).</b> STY01's LocalAdmin lists STY01 and sees both seeded rows
    /// (200); STY02's LocalAdmin lists STY02 and sees none of STY01's ids, and STY02's list holds only STY02
    /// rows.
    /// <para><b>Product code between the readings:</b> the list query's binding to the address's
    /// organisation.</para>
    /// <para><b>Red conditions (F).</b> A list that ignores the organisation returns STY01's rows under STY02's
    /// address, so the "none of STY01's ids" assertion fails; STY02 has one row of its own, so an empty list
    /// from a broken query does not pass either.</para>
    /// </summary>
    [Fact]
    public async Task ListThroughTheOtherAddress_ShowsNoForeignRows()
    {
        var own = await _fx.Client("localAdminOfSty01").GetAsync("/api/config/STY01/customary-days");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        var ownIds = (await own.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Select(e => e.GetProperty("customId").GetGuid()).ToList();
        Assert.Contains(_fx.Open01, ownIds);
        Assert.Contains(_fx.Withdrawn01, ownIds);

        var other = await _fx.Client("localAdminOfOtherOrganisation").GetAsync("/api/config/STY02/customary-days");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        var rows = (await other.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal(new[] { _fx.Open02 }, rows.Select(e => e.GetProperty("customId").GetGuid()).ToArray());
        Assert.All(rows, e => Assert.Equal("STY02", e.GetProperty("orgId").GetString()));
    }

    /// <summary>The shared host (see the class summary).</summary>
    public sealed class AccessHost : IAsyncLifetime
    {
        internal TestFixtures.DockerHarness Harness { get; private set; } = null!;
        internal WebApplicationFactory<Program> Host { get; private set; } = null!;
        private StatsTidWebApplicationFactory _factory = null!;

        public Guid Open01 { get; } = Guid.NewGuid();
        public Guid Withdrawn01 { get; } = Guid.NewGuid();
        public Guid Open02 { get; } = Guid.NewGuid();
        public Guid OpenDel { get; } = Guid.NewGuid();
        public Guid WithdrawnDel { get; } = Guid.NewGuid();

        public async Task InitializeAsync()
        {
            Harness = await TestFixtures.DockerHarness.StartAsync();
            await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Harness.ConnectionString);
            _factory = new StatsTidWebApplicationFactory(Harness.ConnectionString);
            Host = _factory.WithFixedToday(TransferWriteTests.Clock);
            _ = Host.CreateClient();

            await using var conn = new NpgsqlConnection(Harness.ConnectionString);
            await conn.OpenAsync();
            await ExecAsync(conn,
                """
                INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path)
                VALUES ('STYDEL', 'S148 deleted institution', 'ORGANISATION', 'MIN01', '/MIN01/STYDEL/')
                """);
            await SeedRowAsync(conn, Open01, "STY01", 5, 15, null);
            await SeedRowAsync(conn, Withdrawn01, "STY01", 8, 3, new DateOnly(2026, 11, 2));
            await SeedRowAsync(conn, Open02, "STY02", 5, 15, null);
            await SeedRowAsync(conn, OpenDel, "STYDEL", 5, 15, null);
            await SeedRowAsync(conn, WithdrawnDel, "STYDEL", 8, 3, new DateOnly(2026, 11, 2));
            await ExecAsync(conn, "UPDATE organizations SET is_active = FALSE WHERE org_id = 'STYDEL'");
        }

        public async Task DisposeAsync()
        {
            Host?.Dispose();
            _factory?.Dispose();
            if (Harness is not null)
                await Harness.DisposeAsync();
        }

        private static async Task SeedRowAsync(NpgsqlConnection conn, Guid id, string org, int month, int day, DateOnly? validTo)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO customary_days (custom_id, org_id, recurring_month, recurring_day, kind, part, valid_from, valid_to, created_by)
                VALUES (@c, @o, @m, @d, 'CLOSURE_DAY', 'WHOLE_DAY', DATE '2026-01-05', @t, 'SEEDED_S148')
                """, conn);
            cmd.Parameters.AddWithValue("c", id);
            cmd.Parameters.AddWithValue("o", org);
            cmd.Parameters.AddWithValue("m", (short)month);
            cmd.Parameters.AddWithValue("d", (short)day);
            cmd.Parameters.Add(new NpgsqlParameter("t", NpgsqlTypes.NpgsqlDbType.Date) { Value = (object?)validTo ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task ExecAsync(NpgsqlConnection conn, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>Every organisation the arms touch, rows plus audit rows plus events, plus the seeded rows' state.</summary>
        public async Task<string> SnapshotAllAsync(params Guid[] extraIds)
        {
            var parts = new List<string>();
            foreach (var org in new[] { "STY01", "STY02", "STY99", "STYDEL" })
            {
                var ids = org switch
                {
                    "STY01" => new[] { Open01, Withdrawn01 }.Concat(extraIds).ToArray(),
                    "STY02" => new[] { Open02 },
                    "STYDEL" => new[] { OpenDel, WithdrawnDel },
                    _ => Array.Empty<Guid>(),
                };
                parts.Add(org + "=" + await CustomaryDaysTests.SnapshotAsync(Harness.ConnectionString, org, ids));
            }
            return string.Join('#', parts);
        }

        /// <summary>The actor of an arm, by name.</summary>
        public HttpClient Client(string actor) => actor switch
        {
            "localAdminOfSty01" => CustomaryDaysTests.LocalAdminClient(Host, "LADM_S148_ACC_1", "STY01"),
            "localAdminOfOtherOrganisation" => CustomaryDaysTests.LocalAdminClient(Host, "LADM_S148_ACC_2", "STY02"),
            "mixedRole" => CustomaryDaysTests.MixedRoleClient(Host, "LADM_S148_ACC_M"),
            "localHr" => TransferWriteTests.LocalHrClient(Host, "LHR_S148_ACC", "STY01"),
            "employee" => RoleClient("EMP_S148_ACC", StatsTidRoles.Employee, "STY01"),
            "localLeaderPrimary" => RoleClient("LLEAD_S148_ACC", StatsTidRoles.LocalLeader, "STY01"),
            "localAdminWithoutScopes" => NoScopeClient("LADM_S148_ACC_N"),
            "parentLocalAdmin" => CustomaryDaysTests.LocalAdminClient(Host, "LADM_S148_ACC_P", "MIN01"),
            "globalAdmin" => TransferWriteTests.GlobalAdminClient(Host, "GADM_S148_ACC"),
            "deletedOrganisationLocalAdmin" => CustomaryDaysTests.LocalAdminClient(Host, "LADM_S148_ACC_D", "STYDEL"),
            _ => throw new ArgumentOutOfRangeException(nameof(actor)),
        };

        private HttpClient RoleClient(string employeeId, string role, string orgId)
        {
            var client = Host.CreateClient();
            var token = TransferWriteTests.TokenService().GenerateToken(
                employeeId: employeeId, name: employeeId, role: role, agreementCode: "HK", orgId: orgId,
                scopes: new[] { new RoleScope(role, orgId, "ORG_ONLY") });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        /// <summary>The role LocalAdmin, no <c>scopes</c> claim at all.</summary>
        private HttpClient NoScopeClient(string employeeId)
        {
            var client = Host.CreateClient();
            var token = TransferWriteTests.TokenService().GenerateToken(
                employeeId: employeeId, name: employeeId, role: StatsTidRoles.LocalAdmin,
                agreementCode: "HK", orgId: "STY01");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }
    }
}
