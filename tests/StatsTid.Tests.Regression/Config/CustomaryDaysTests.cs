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
/// S148 / TASK-14834-T (AC-C10a, AC-C10b) — an institution's customary days, server side (C-10, ADR-043 D9).
///
/// <para>
/// <b>What this suite is about, in plain language.</b> A local administrator can record the institution's
/// own days off (5 June from noon, a closure day after Ascension), stop one from a date ("withdraw"), and
/// bring it back from a later date ("reinstate"). Every one of those three writes leaves its row, one audit
/// row, one <c>CustomaryDayChanged</c> event and one audit-projection row the institution's administrators
/// can read. A reinstatement is a NEW row: the stopped period stays visible as a gap. An administrator of
/// another institution, or one whose only covering scope is not an administrator's, is refused with 403 and
/// nothing is written; a row of one institution cannot be reached through another institution's address.
/// </para>
///
/// <para>
/// <b>Fixture.</b> A Backend host with the clock fixed at Tuesday 10 November 2026 (PAT-008), one container
/// per class. The actor is a LocalAdmin of STY01 with employee id <c>LADM_S148_C10</c>. Docker is
/// unavailable on the authoring machine: these facts are CI-pending (a throwaway local cluster is evidence
/// only). The <c>internal static</c> members are reused by <see cref="CustomaryDaysValidationTests"/>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class CustomaryDaysTests : IAsyncLifetime
{
    internal const string Actor = "LADM_S148_C10";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    private string Cs => _harness.ConnectionString;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // PAT-008 boot order: the fixed host boots first.
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

    // ═════════════════════════════════════════════════════════════════════
    // AC-C10a
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI), AC-C10a.</b> The worked example: a LocalAdmin of STY01 creates "5 June, paid day off from
    /// 12:00, given to all, valid from Monday 5 January 2026" (201, version 1), withdraws it from Monday
    /// 2 November 2026 (200, version 2), and reinstates it from Tuesday 10 November 2026 (201, a NEW
    /// custom id at version 1, the withdrawn row left as it was). Each write leaves one audit row, one event
    /// on <c>customary-days-STY01</c> and one audit-projection row targeting STY01.
    /// <para><b>Product code between the readings:</b> the three POSTs (validation, then one transaction:
    /// organisation lock, row, audit row, outbox event, audit-projection row) and the GET.</para>
    /// <para><b>Red conditions (F).</b> On the code before TASK-14834 no customs route exists, so the create
    /// answers 404 and <c>Assert.Equal(HttpStatusCode.Created, …)</c> fails first.</para>
    /// </summary>
    [Fact]
    public async Task CreateWithdrawReinstate_EachAudited()
    {
        var client = LocalAdminClient(_host, Actor, "STY01");
        const string baseUrl = "/api/config/STY01/customary-days";

        // 1. Create.
        var createRsp = await client.PostAsJsonAsync(baseUrl, new
        {
            recurringMonth = 6, recurringDay = 5, kind = "PAID_DAY_OFF", part = "FROM_TIME",
            partFromTime = "12:00:00", paidMode = "GIVEN_TO_ALL", validFrom = "2026-01-05",
        });
        Assert.Equal(HttpStatusCode.Created, createRsp.StatusCode);
        var created = await createRsp.Content.ReadFromJsonAsync<JsonElement>();
        var c1 = created.GetProperty("customId").GetGuid();
        Assert.Equal(1L, created.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("validTo").ValueKind);

        var row1 = await ReadRowAsync(Cs, c1);
        Assert.Equal(new CustomRow(c1, "STY01", null, 6, 5, null, "PAID_DAY_OFF", "FROM_TIME",
            new TimeOnly(12, 0), null, "GIVEN_TO_ALL", new DateOnly(2026, 1, 5), null, 1L, Actor), row1);

        var audit1 = await ReadAuditAsync(Cs, c1);
        var a1 = Assert.Single(audit1);
        Assert.Equal(("CREATED", (long?)null, 1L, Actor), (a1.Action, a1.VersionBefore, a1.VersionAfter, a1.ActorId));
        Assert.Null(a1.PreviousData);
        Assert.Equal(c1, a1.NewData!.Value.GetProperty("customId").GetGuid());
        Assert.Equal(6, a1.NewData.Value.GetProperty("recurringMonth").GetInt32());
        Assert.Equal(5, a1.NewData.Value.GetProperty("recurringDay").GetInt32());
        Assert.Equal("PAID_DAY_OFF", a1.NewData.Value.GetProperty("kind").GetString());
        Assert.Equal("FROM_TIME", a1.NewData.Value.GetProperty("part").GetString());
        Assert.Equal("12:00:00", a1.NewData.Value.GetProperty("partFromTime").GetString());
        Assert.Equal("GIVEN_TO_ALL", a1.NewData.Value.GetProperty("paidMode").GetString());
        Assert.Equal("2026-01-05", a1.NewData.Value.GetProperty("validFrom").GetString());
        Assert.Equal(1L, a1.NewData.Value.GetProperty("version").GetInt64());

        var ev1 = Assert.Single(await ReadEventsAsync(Cs, "STY01", c1));
        Assert.Equal("CREATED", ev1.Payload.GetProperty("action").GetString());
        Assert.Equal(1L, ev1.Payload.GetProperty("versionAfter").GetInt64());
        await AssertProjectionAsync(Cs, ev1.EventId, c1);

        // 2. Withdraw.
        var withdrawRsp = await client.PostAsJsonAsync($"{baseUrl}/{c1}/withdraw", new { validTo = "2026-11-02" });
        Assert.Equal(HttpStatusCode.OK, withdrawRsp.StatusCode);
        var row2 = await ReadRowAsync(Cs, c1);
        Assert.Equal(row1 with { Version = 2L, ValidTo = new DateOnly(2026, 11, 2) }, row2);

        var audit2 = await ReadAuditAsync(Cs, c1);
        Assert.Equal(2, audit2.Count);
        var a2 = audit2[1];
        Assert.Equal(("WITHDRAWN", (long?)1L, 2L, Actor), (a2.Action, a2.VersionBefore, a2.VersionAfter, a2.ActorId));

        var ev2All = await ReadEventsAsync(Cs, "STY01", c1);
        Assert.Equal(2, ev2All.Count);
        var ev2 = ev2All[1];
        Assert.Equal("WITHDRAWN", ev2.Payload.GetProperty("action").GetString());
        Assert.Equal("2026-11-02", ev2.Payload.GetProperty("validTo").GetString());
        Assert.Equal(1L, ev2.Payload.GetProperty("versionBefore").GetInt64());
        Assert.Equal(2L, ev2.Payload.GetProperty("versionAfter").GetInt64());
        await AssertProjectionAsync(Cs, ev2.EventId, c1);

        // 3. Reinstate: a NEW row.
        var reinstateRsp = await client.PostAsJsonAsync($"{baseUrl}/{c1}/reinstate", new { validFrom = "2026-11-10" });
        Assert.Equal(HttpStatusCode.Created, reinstateRsp.StatusCode);
        var reinstated = await reinstateRsp.Content.ReadFromJsonAsync<JsonElement>();
        var c2 = reinstated.GetProperty("customId").GetGuid();
        Assert.NotEqual(c1, c2);

        var rowNew = await ReadRowAsync(Cs, c2);
        Assert.Equal(row1 with { CustomId = c2, ValidFrom = new DateOnly(2026, 11, 10), ValidTo = null, Version = 1L }, rowNew);
        // The withdrawn row is left as it was: the stopped period stays a gap.
        Assert.Equal(row2, await ReadRowAsync(Cs, c1));
        Assert.Equal(2L, await ScalarLongAsync(Cs, "SELECT COUNT(*) FROM customary_days WHERE org_id = 'STY01'"));

        var audit3 = Assert.Single(await ReadAuditAsync(Cs, c2));
        Assert.Equal(("REINSTATED", (long?)null, 1L, Actor), (audit3.Action, audit3.VersionBefore, audit3.VersionAfter, audit3.ActorId));
        Assert.Equal(c1, audit3.PreviousData!.Value.GetProperty("customId").GetGuid());
        Assert.Equal("2026-11-02", audit3.PreviousData.Value.GetProperty("validTo").GetString());
        Assert.Equal(2L, audit3.PreviousData.Value.GetProperty("version").GetInt64());

        var ev3 = Assert.Single(await ReadEventsAsync(Cs, "STY01", c2));
        Assert.Equal("REINSTATED", ev3.Payload.GetProperty("action").GetString());
        Assert.Equal(c1, ev3.Payload.GetProperty("precedingCustomId").GetGuid());
        await AssertProjectionAsync(Cs, ev3.EventId, c2);

        // Finally the list carries both rows with the values above.
        var listRsp = await client.GetAsync(baseUrl);
        Assert.Equal(HttpStatusCode.OK, listRsp.StatusCode);
        var list = (await listRsp.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var l1 = Assert.Single(list, e => e.GetProperty("customId").GetGuid() == c1);
        var l2 = Assert.Single(list, e => e.GetProperty("customId").GetGuid() == c2);
        Assert.Equal("2026-11-02", l1.GetProperty("validTo").GetString());
        Assert.Equal(2L, l1.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, l2.GetProperty("validTo").ValueKind);
        Assert.Equal("2026-11-10", l2.GetProperty("validFrom").GetString());
        Assert.Equal(1L, l2.GetProperty("version").GetInt64());
        Assert.Equal(5, l2.GetProperty("recurringDay").GetInt32());
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C10b
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI), AC-C10b.</b> Six refusals, each writing nothing: a LocalAdmin of STY02 creates at, and
    /// withdraws through, STY01's address (403 "Actor scope does not cover target organization"); the same
    /// actor withdraws STY01's row through STY02's own address (404: the row is bound to the address's
    /// organisation); a LocalAdmin of STY02 who also holds a LocalLeader scope over STY01 cannot create at
    /// STY01 (the non-admin scope must not admit an admin write, FAIL-001's class); a LocalHR of STY01 is
    /// refused at the policy; a GlobalAdmin addressing the unseeded STY99 gets 403 "Organization not found".
    /// <para><b>Product code between the readings:</b> the routes' policy and scope check, and for the
    /// STY02-address arm the locked, organisation-bound row read.</para>
    /// <para><b>Red conditions (F).</b> On the code before TASK-14834 arm 1 answers 404 (no route), so
    /// <c>Assert.Equal(HttpStatusCode.Forbidden, …)</c> fails first. Arms 3 and 6 also answer 404 there;
    /// arm 3 discriminates only once the routes exist (a lookup without the organisation would answer
    /// 200).</para>
    /// </summary>
    [Fact]
    public async Task LocalAdminOfOtherOrganisation_403()
    {
        var sty01Admin = LocalAdminClient(_host, Actor, "STY01");
        var createC3 = await sty01Admin.PostAsJsonAsync("/api/config/STY01/customary-days", new
        {
            singleDate = "2026-05-15", kind = "CLOSURE_DAY", part = "WHOLE_DAY", validFrom = "2026-01-05",
        });
        Assert.Equal(HttpStatusCode.Created, createC3.StatusCode);
        var c3 = (await createC3.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("customId").GetGuid();

        var sty02Admin = LocalAdminClient(_host, "LADM_S148_C10_O", "STY02");
        var mixed = MixedRoleClient(_host, "LADM_S148_C10_M");
        var hr = TransferWriteTests.LocalHrClient(_host, "LHR_S148_C10", "STY01");
        var global = TransferWriteTests.GlobalAdminClient(_host, "GADM_S148_C10");

        var createBody = new { singleDate = "2026-12-24", kind = "CLOSURE_DAY", part = "WHOLE_DAY", validFrom = "2026-01-05" };
        var withdrawBody = new { validTo = "2026-11-02" };

        // 1. A LocalAdmin of STY02 creates at STY01's address.
        var before = await SnapshotAsync(Cs, "STY01", c3) + "#" + await SnapshotAsync(Cs, "STY02");
        var r1 = await sty02Admin.PostAsJsonAsync("/api/config/STY01/customary-days", createBody);
        Assert.Equal(HttpStatusCode.Forbidden, r1.StatusCode);
        await AssertBodyAsync(r1, ("error", "Access denied"), ("reason", "Actor scope does not cover target organization"));
        Assert.Equal(before, await SnapshotAsync(Cs, "STY01", c3) + "#" + await SnapshotAsync(Cs, "STY02"));

        // 2. The same actor withdraws C3 through STY01's address.
        var r2 = await sty02Admin.PostAsJsonAsync($"/api/config/STY01/customary-days/{c3}/withdraw", withdrawBody);
        Assert.Equal(HttpStatusCode.Forbidden, r2.StatusCode);
        await AssertBodyAsync(r2, ("error", "Access denied"), ("reason", "Actor scope does not cover target organization"));
        Assert.Equal(before, await SnapshotAsync(Cs, "STY01", c3) + "#" + await SnapshotAsync(Cs, "STY02"));

        // 3. The same actor withdraws C3 through STY02's own address: the row is bound to the address's organisation.
        var r3 = await sty02Admin.PostAsJsonAsync($"/api/config/STY02/customary-days/{c3}/withdraw", withdrawBody);
        Assert.Equal(HttpStatusCode.NotFound, r3.StatusCode);
        await AssertBodyAsync(r3, ("error", "Customary day not found"));
        Assert.Equal(before, await SnapshotAsync(Cs, "STY01", c3) + "#" + await SnapshotAsync(Cs, "STY02"));

        // 4. A LocalAdmin of STY02 who also holds a LocalLeader scope over STY01 creates at STY01's address.
        var r4 = await mixed.PostAsJsonAsync("/api/config/STY01/customary-days", createBody);
        Assert.Equal(HttpStatusCode.Forbidden, r4.StatusCode);
        await AssertBodyAsync(r4, ("error", "Access denied"), ("reason", "Actor scope does not cover target organization"));
        Assert.Equal(before, await SnapshotAsync(Cs, "STY01", c3) + "#" + await SnapshotAsync(Cs, "STY02"));

        // 5. A LocalHR of STY01: refused at the policy (its 403 has no house body).
        var r5 = await hr.PostAsJsonAsync("/api/config/STY01/customary-days", createBody);
        Assert.Equal(HttpStatusCode.Forbidden, r5.StatusCode);
        Assert.Equal(before, await SnapshotAsync(Cs, "STY01", c3) + "#" + await SnapshotAsync(Cs, "STY02"));

        // 6. A GlobalAdmin on an organisation that does not exist is refused too.
        var r6 = await global.PostAsJsonAsync("/api/config/STY99/customary-days", createBody);
        Assert.Equal(HttpStatusCode.Forbidden, r6.StatusCode);
        await AssertBodyAsync(r6, ("error", "Access denied"), ("reason", "Organization not found"));
        Assert.Equal(0L, await ScalarLongAsync(Cs, "SELECT COUNT(*) FROM customary_days WHERE org_id = 'STY99'"));
        Assert.Equal(0L, await ScalarLongAsync(Cs, "SELECT COUNT(*) FROM customary_days_audit WHERE org_id = 'STY99'"));
        Assert.Equal(0L, await ScalarLongAsync(Cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = 'customary-days-STY99'"));
        Assert.Equal(before, await SnapshotAsync(Cs, "STY01", c3) + "#" + await SnapshotAsync(Cs, "STY02"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Shared helpers (internal: reused by CustomaryDaysValidationTests)
    // ═════════════════════════════════════════════════════════════════════

    internal sealed record CustomRow(
        Guid CustomId, string OrgId, string? AgreementCode, int? RecurringMonth, int? RecurringDay,
        DateOnly? SingleDate, string Kind, string Part, TimeOnly? PartFromTime, decimal? PartHours,
        string? PaidMode, DateOnly ValidFrom, DateOnly? ValidTo, long Version, string CreatedBy);

    internal sealed record AuditRow(
        string Action, long? VersionBefore, long? VersionAfter, string ActorId,
        JsonElement? PreviousData, JsonElement? NewData);

    internal sealed record EventRow(Guid EventId, JsonElement Payload);

    /// <summary>A LocalAdmin whose only scope is ORG_ONLY over <paramref name="orgId"/> (the <c>LocalHrClient</c> shape).</summary>
    internal static HttpClient LocalAdminClient(WebApplicationFactory<Program> host, string employeeId, string orgId)
    {
        var client = host.CreateClient();
        var token = TransferWriteTests.TokenService().GenerateToken(
            employeeId: employeeId, name: employeeId, role: StatsTidRoles.LocalAdmin,
            agreementCode: "HK", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, orgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A LocalAdmin of STY02 who also holds a LocalLeader scope over STY01.</summary>
    internal static HttpClient MixedRoleClient(WebApplicationFactory<Program> host, string employeeId)
    {
        var client = host.CreateClient();
        var token = TransferWriteTests.TokenService().GenerateToken(
            employeeId: employeeId, name: employeeId, role: StatsTidRoles.LocalAdmin,
            agreementCode: "HK", orgId: "STY02",
            scopes: new[]
            {
                new RoleScope(StatsTidRoles.LocalAdmin, "STY02", "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalLeader, "STY01", "ORG_ONLY"),
            });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Asserts the JSON object has exactly these string properties and no others.</summary>
    internal static async Task AssertBodyAsync(HttpResponseMessage rsp, params (string Name, string Value)[] expected)
    {
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var actual = body.EnumerateObject().Select(p => (p.Name, p.Value.GetString()!)).ToArray();
        Assert.Equal(expected, actual);
    }

    /// <summary>Everything a refused write must leave alone: the organisation's rows, audit rows and events, plus the given rows' state.</summary>
    internal static async Task<string> SnapshotAsync(string cs, string orgId, params Guid[] customIds)
    {
        var rows = await ScalarLongAsync(cs, "SELECT COUNT(*) FROM customary_days WHERE org_id = @o", ("o", orgId));
        var audit = await ScalarLongAsync(cs, "SELECT COUNT(*) FROM customary_days_audit WHERE org_id = @o", ("o", orgId));
        var events = await ScalarLongAsync(cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", ("s", $"customary-days-{orgId}"));
        var states = new List<string>();
        foreach (var id in customIds)
        {
            var r = await ReadRowAsync(cs, id);
            states.Add($"{r.Version}/{r.ValidTo}");
        }
        return string.Join('|', rows, audit, events, string.Join(';', states));
    }

    internal static async Task<CustomRow> ReadRowAsync(string cs, Guid customId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT custom_id, org_id, agreement_code, recurring_month, recurring_day, single_date, kind, part,
                   part_from_time, part_hours, paid_mode, valid_from, valid_to, version, created_by
            FROM customary_days WHERE custom_id = @c
            """, conn);
        cmd.Parameters.AddWithValue("c", customId);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), $"no customary_days row {customId}");
        T? N<T>(int i) where T : struct => r.IsDBNull(i) ? null : r.GetFieldValue<T>(i);
        return new CustomRow(
            r.GetGuid(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            N<short>(3), N<short>(4), N<DateOnly>(5), r.GetString(6), r.GetString(7),
            N<TimeOnly>(8), N<decimal>(9), r.IsDBNull(10) ? null : r.GetString(10),
            r.GetFieldValue<DateOnly>(11), N<DateOnly>(12), r.GetInt64(13), r.GetString(14));
    }

    internal static async Task<IReadOnlyList<AuditRow>> ReadAuditAsync(string cs, Guid customId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after, actor_id, previous_data::text, new_data::text
            FROM customary_days_audit WHERE custom_id = @c ORDER BY audit_id
            """, conn);
        cmd.Parameters.AddWithValue("c", customId);
        var rows = new List<AuditRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            JsonElement? J(int i) => r.IsDBNull(i) ? null : JsonSerializer.Deserialize<JsonElement>(r.GetString(i));
            rows.Add(new AuditRow(r.GetString(0), r.IsDBNull(1) ? null : r.GetInt64(1),
                r.IsDBNull(2) ? null : r.GetInt64(2), r.GetString(3), J(4), J(5)));
        }
        return rows;
    }

    /// <summary>The outbox events of the organisation's stream whose payload names <paramref name="customId"/>, oldest first.</summary>
    internal static async Task<IReadOnlyList<EventRow>> ReadEventsAsync(string cs, string orgId, Guid customId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id, event_payload::text FROM outbox_events
            WHERE stream_id = @s AND event_type = 'CustomaryDayChanged' AND event_payload->>'customId' = @c
            ORDER BY outbox_id
            """, conn);
        cmd.Parameters.AddWithValue("s", $"customary-days-{orgId}");
        cmd.Parameters.AddWithValue("c", customId.ToString());
        var rows = new List<EventRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new EventRow(r.GetGuid(0), JsonSerializer.Deserialize<JsonElement>(r.GetString(1))));
        return rows;
    }

    /// <summary>One audit-projection row for the event, tenant-targeted at STY01, resource = the custom.</summary>
    private static async Task AssertProjectionAsync(string cs, Guid eventId, Guid customId)
    {
        Assert.Equal(1L, await ScalarLongAsync(cs, "SELECT COUNT(*) FROM audit_projection WHERE event_id = @e", ("e", eventId)));
        Assert.Equal("TENANT_TARGETED", await ScalarStringAsync(cs, "SELECT visibility_scope FROM audit_projection WHERE event_id = @e", ("e", eventId)));
        Assert.Equal("STY01", await TransferWriteTests.ProjectionTargetOrgAsync(cs, eventId));
        Assert.Equal(customId.ToString(), await TransferWriteTests.ProjectionTargetResourceAsync(cs, eventId));
    }

    internal static Task<long> ScalarLongAsync(string cs, string sql, params (string Name, object Value)[] p)
        => TransferWriteTests.ScalarLongAsync(cs, sql, p);

    internal static Task<string?> ScalarStringAsync(string cs, string sql, params (string Name, object Value)[] p)
        => TransferWriteTests.ScalarStringAsync(cs, sql, p);
}
