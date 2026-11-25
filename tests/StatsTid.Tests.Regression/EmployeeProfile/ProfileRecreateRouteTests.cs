using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Config;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Security;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S148 / TASK-14851-T (AC-Q268a, b, c, d, e, i, j; QUAL-268; the TASK-14851 security consult's added negatives) - after HR
/// closes a person's employment profile, the same profile edit, sent with the header "only if nothing is there"
/// (<c>If-None-Match: *</c>), creates a new one from a date HR picks; and everything that must not create one is refused
/// with nothing written.
///
/// <para>
/// <b>In plain language.</b> A close leaves a person employed but without a profile, so payroll refuses every month from then
/// on. These facts prove the repair: HR re-creates the profile from a chosen date (one new row, one audit row, one event, one
/// projection row), and the read of "no profile" says whether and from when that is possible. They also prove who and what is
/// refused: a profile that already covers today or is scheduled, a date in the future or before the last profile ended, a
/// person's own record (every role, GlobalAdmin included), a person moved to another organisation while HR was saving, an
/// actor without HR cover over the person's organisation, a bad header, and a fraction that is not above zero, has more than
/// three decimals or does not fit the column. Every refusal leaves the person's rows, audit rows, events, projection rows,
/// worklist rows and version exactly as they were.
/// </para>
///
/// <para>
/// <b>Fixture.</b> One database per fact (<see cref="DatedConfigDockerFixture"/>); two Backend hosts over it where a fact
/// needs two clocks (the close, then the re-create). Both hosts are booted before anyone is seeded (PAT-008). Unless named
/// the person is Anna (an HK person of STY01, <c>SeedPersonAsync</c>, a profile from 0001-01-01) and the actor is Lise, a
/// LocalHR of STY01 only (<c>HR_S148_Q268</c>, not a seeded person). "Close" is the HR route at the host's clock (204);
/// "re-create" is the PUT with <c>If-None-Match: *</c> and no <c>If-Match</c>. "Nothing written" is
/// <see cref="SelfChangeRefusalTests.WriteTrailAsync"/> (the person's profile and agreement rows, the audit tables, every event
/// on a stream ending in the person's id, the projection and worklist rows, the employment columns) plus
/// <c>users.version</c>, taken before and after the one request. Every value, status, code and reason is a literal, never a
/// production constant. Docker-gated: <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileRecreateRouteTests : DatedConfigDockerFixture, IDisposable
{
    private const string LiseId = "HR_S148_Q268";

    private const string SelfReason =
        "Own agreement code and employee profile cannot be modified; a second administrator must perform this change";

    private const string DepartedReason = "A departed employee cannot act on their own record";
    private const string NotCoveredReason = "Actor scope does not cover target organization";
    private const string DriftError =
        "The user's organisation changed while this request was being processed; refresh and retry.";

    private static readonly DateOnly Mar13 = new(2026, 3, 13);   // a Friday
    private static readonly DateOnly Mar16 = new(2026, 3, 16);   // a Monday
    private static readonly DateOnly Mar20 = new(2026, 3, 20);   // a Friday
    private static readonly DateOnly Mar25 = new(2026, 3, 25);   // a Wednesday
    private static readonly DateOnly Apr10 = new(2026, 4, 10);   // a Friday

    private readonly List<WebApplicationFactory<Program>> _hosts = new();

    public void Dispose()
    {
        foreach (var host in _hosts)
            host.Dispose();
    }

    // ─── hosts, people, requests ───

    /// <summary>A Backend host pinned at <paramref name="day"/>, booted at once so its seeders have run before anyone is seeded.</summary>
    private WebApplicationFactory<Program> HostAt(DateOnly day)
    {
        var host = Factory.WithFixedToday(day);
        _hosts.Add(host);
        _ = host.CreateClient();
        return host;
    }

    private static HttpClient Lise(WebApplicationFactory<Program> host) => LocalHrClient(host, LiseId, "STY01");

    private static HttpClient TokenClient(
        WebApplicationFactory<Program> host, string id, string role, params RoleScope[] scopes)
    {
        var client = host.CreateClient();
        var token = TokenService().GenerateToken(
            employeeId: id, name: id, role: role, agreementCode: "HK", orgId: scopes[0].OrgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private Task<string> PersonAsync(string org = "STY01") => SeedPersonAsync(ConnectionString, org, "HK");

    private Task<long> VersionAsync(string id) =>
        ScalarLongAsync(ConnectionString, "SELECT version FROM users WHERE user_id = @u", ("u", id));

    /// <summary>The close as the HR route does it: DELETE with the user's version in If-Match; answers 204.</summary>
    private async Task CloseAsync(HttpClient client, string id)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{id}");
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{await VersionAsync(id)}\"");
        using var close = await client.SendAsync(request);
        Assert.True(close.StatusCode == HttpStatusCode.NoContent, $"the close: {(int)close.StatusCode} {await close.Content.ReadAsStringAsync()}");
    }

    /// <summary>Anna, with her profile closed through the route by Lise at <paramref name="closeHost"/>'s clock.</summary>
    private async Task<string> ClosedPersonAsync(WebApplicationFactory<Program> closeHost)
    {
        var id = await PersonAsync();
        await CloseAsync(Lise(closeHost), id);
        return id;
    }

    private static Dictionary<string, object?> Body(
        string effectiveFrom, decimal fraction = 1.000m, string? position = null, string? category = null, bool? carry = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["effectiveFrom"] = effectiveFrom,
            ["partTimeFraction"] = fraction,
            ["position"] = position,
            ["employmentCategory"] = category,
        };
        if (carry is not null)
            body["carryForwardToScheduledChange"] = carry;
        return body;
    }

    /// <summary>The re-create: PUT with <c>If-None-Match: *</c>, or the header sets a fact names instead.</summary>
    private static Task<HttpResponseMessage> RecreateAsync(
        HttpClient client, string id, object body, params (string Name, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{id}") { Content = JsonContent.Create(body) };
        if (headers.Length == 0)
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        foreach (var (name, value) in headers)
            request.Headers.TryAddWithoutValidation(name, value);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, string id, object body, long ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{id}") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string id) =>
        client.GetAsync($"/api/admin/employee-profiles/{id}");

    // ─── reads ───

    private sealed record Reply(HttpStatusCode Status, string Text, JsonElement Json);

    private static async Task<Reply> ReadAsync(HttpResponseMessage rsp, HttpStatusCode expected, string what)
    {
        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == expected, $"{what}: expected {(int)expected}, got {(int)rsp.StatusCode}: {text}");
        var json = string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return new Reply(rsp.StatusCode, text, json);
    }

    private static void AssertMembers(JsonElement element, params string[] expected) =>
        Assert.Equal(
            expected.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());

    /// <summary>403 <c>{ error: "Access denied", reason }</c> with exactly those two members.</summary>
    private static async Task AssertForbiddenAsync(HttpResponseMessage rsp, string reason, string what)
    {
        var reply = await ReadAsync(rsp, HttpStatusCode.Forbidden, what);
        AssertMembers(reply.Json, "error", "reason");
        Assert.Equal("Access denied", reply.Json.GetProperty("error").GetString());
        Assert.Equal(reason, reply.Json.GetProperty("reason").GetString());
    }

    /// <summary>400 <c>{ error: "Employee profile validation failed", fields: [ one entry ] }</c>.</summary>
    private static async Task AssertValidationAsync(
        HttpResponseMessage rsp, string field, string code, string[]? nearestValid, string what)
    {
        var reply = await ReadAsync(rsp, HttpStatusCode.BadRequest, what);
        AssertMembers(reply.Json, "error", "fields");
        Assert.Equal("Employee profile validation failed", reply.Json.GetProperty("error").GetString());
        var entry = Assert.Single(reply.Json.GetProperty("fields").EnumerateArray().ToList());
        AssertMembers(entry, "field", "code", "nearestValid");
        Assert.Equal(field, entry.GetProperty("field").GetString());
        Assert.Equal(code, entry.GetProperty("code").GetString());
        var nearest = entry.GetProperty("nearestValid");
        if (nearestValid is null)
            Assert.Equal(JsonValueKind.Null, nearest.ValueKind);
        else
            Assert.Equal(nearestValid, nearest.EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>Everything a refused request must leave alone: the shared write trail plus <c>users.version</c>.</summary>
    private async Task<string> SnapshotAsync(string id) =>
        await SelfChangeRefusalTests.WriteTrailAsync(ConnectionString, id) + "|v=" + await VersionAsync(id);

    private sealed record ProfileRow(Guid Id, DateOnly From, DateOnly? To, decimal Fraction, string? Position, string Category);

    private async Task<List<ProfileRow>> RowsAsync(string id)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT profile_id, effective_from, effective_to, part_time_fraction, position, employment_category
            FROM employee_profiles WHERE employee_id = @e ORDER BY effective_from, effective_to NULLS LAST
            """, conn);
        cmd.Parameters.AddWithValue("e", id);
        var rows = new List<ProfileRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ProfileRow(
                reader.GetGuid(0), reader.GetFieldValue<DateOnly>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                reader.GetDecimal(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
        }
        return rows;
    }

    private sealed record AuditRow(long AuditId, string Action, string? Previous, string NewData, long? Before, long? After, string Actor);

    private async Task<List<AuditRow>> AuditRowsAsync(string id, long afterAuditId = 0)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT audit_id, action, previous_data::text, new_data::text, version_before, version_after, actor_id
            FROM employee_profile_audit WHERE employee_id = @e AND audit_id > @after ORDER BY audit_id
            """, conn);
        cmd.Parameters.AddWithValue("e", id);
        cmd.Parameters.AddWithValue("after", afterAuditId);
        var rows = new List<AuditRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetInt64(5), reader.GetString(6)));
        }
        return rows;
    }

    private async Task<long> MaxAuditIdAsync(string id) =>
        await ScalarAsync<long?>("SELECT MAX(audit_id) FROM employee_profile_audit WHERE employee_id = @e", ("e", id)) ?? 0;

    private static JsonElement Prop(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(name).Clone();
    }

    private Task<long> StreamEventCountAsync(string id) => CountAsync(
        "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", ("s", $"employee-profile-{id}"));

    // ═══ AC-Q268a ═══

    /// <summary><b>F (CI).</b> AC-Q268a (plus consult row 23). Anna (employment start Tuesday 17 November 2025) is closed by Lise on
    /// Monday 16 March 2026. On Friday 10 April 2026 the GET answers 404 whose body has exactly the six members
    /// <c>error</c>, <c>canCreate</c> true, <c>uncoveredSince</c> "2026-03-16", <c>scheduledFrom</c> null, <c>lastPartTimeFraction</c>
    /// 1.000, <c>lastPosition</c> null, and no employment date (neither a member nor the start date's text). Lise re-creates from
    /// Wednesday 25 March 2026, 0.800, "Fuldmægtig": 200, the profile as of today, <c>version</c> = users.version after = before + 1,
    /// <c>scheduled</c> null, ETag that version. Anna's rows are exactly the closed row (unchanged) and one open row from 25 March;
    /// one new <c>CREATED</c> audit row (source ADMIN_PROFILE_CREATE, versions before and before + 1, actor Lise), one new
    /// <c>users_audit</c> row, one new <c>EmployeeProfileCreated</c> event, one new TENANT_TARGETED projection row for STY01, and no
    /// worklist row. The GET then answers 200 with the new values.
    /// <para><b>Product code between the readings:</b> the close, the GET's 404 read, the PUT's create branch and routed write, the
    /// GET's covered read.</para>
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the GET's 404 body is <c>{ "error" }</c> only, so the member-set
    /// assertion on step 2 fails first. (The re-create would answer 428: the admin-strict parser rejects
    /// <c>If-None-Match: *</c>.)</para></summary>
    [Fact]
    public async Task Recreate_AfterClose_FromChosenDate_OneRowAuditEventProjection()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var anna = await PersonAsync();
        await ExecAsync("UPDATE users SET employment_start_date = DATE '2025-11-17' WHERE user_id = @u", ("u", anna));
        await CloseAsync(Lise(h1), anna);
        var lise = Lise(h2);

        using (var notFound = await GetAsync(lise, anna))
        {
            var reply = await ReadAsync(notFound, HttpStatusCode.NotFound, "the GET after the close");
            AssertMembers(reply.Json, "error", "canCreate", "uncoveredSince", "scheduledFrom", "lastPartTimeFraction", "lastPosition");
            Assert.Equal("Employee profile not found", reply.Json.GetProperty("error").GetString());
            Assert.True(reply.Json.GetProperty("canCreate").GetBoolean());
            Assert.Equal("2026-03-16", reply.Json.GetProperty("uncoveredSince").GetString());
            Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("scheduledFrom").ValueKind);
            Assert.Equal(1.000m, reply.Json.GetProperty("lastPartTimeFraction").GetDecimal());
            Assert.Equal(JsonValueKind.Null, reply.Json.GetProperty("lastPosition").ValueKind);
            Assert.DoesNotContain("2025-11-17", reply.Text, StringComparison.Ordinal);
        }

        var before = await VersionAsync(anna);
        var closedRows = await RowsAsync(anna);
        var auditMark = await MaxAuditIdAsync(anna);
        var usersAuditBefore = await CountAsync("SELECT COUNT(*) FROM users_audit WHERE user_id = @u", ("u", anna));
        var eventsBefore = await StreamEventCountAsync(anna);
        var projectionBefore = await CountAsync("SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @u", ("u", anna));

        using var created = await RecreateAsync(lise, anna, Body("2026-03-25", 0.800m, "Fuldmægtig"));
        var put = await ReadAsync(created, HttpStatusCode.OK, "the re-create");
        AssertMembers(put.Json, "employeeId", "partTimeFraction", "position", "isPartTime", "version", "scheduled");
        Assert.Equal(0.800m, put.Json.GetProperty("partTimeFraction").GetDecimal());
        Assert.Equal("Fuldmægtig", put.Json.GetProperty("position").GetString());
        Assert.True(put.Json.GetProperty("isPartTime").GetBoolean());
        Assert.Equal(JsonValueKind.Null, put.Json.GetProperty("scheduled").ValueKind);
        var after = await VersionAsync(anna);
        Assert.Equal(before + 1, after);
        Assert.Equal(after, put.Json.GetProperty("version").GetInt64());
        Assert.Equal($"\"{after}\"", created.Headers.ETag!.Tag);

        var rows = await RowsAsync(anna);
        Assert.Equal(2, rows.Count);
        Assert.Equal(closedRows[0], rows[0]);
        Assert.Equal((Mar25, (DateOnly?)null, 0.800m, "Fuldmægtig", closedRows[0].Category),
            (rows[1].From, rows[1].To, rows[1].Fraction, rows[1].Position, rows[1].Category));

        var audit = Assert.Single(await AuditRowsAsync(anna, auditMark));
        Assert.Equal("CREATED", audit.Action);
        Assert.Null(audit.Previous);
        Assert.Equal("ADMIN_PROFILE_CREATE", Prop(audit.NewData, "source").GetString());
        Assert.Equal("2026-03-25", Prop(audit.NewData, "effectiveFrom").GetString());
        Assert.Equal(JsonValueKind.Null, Prop(audit.NewData, "effectiveTo").ValueKind);
        Assert.Equal((before, after, LiseId), (audit.Before, audit.After, audit.Actor));
        Assert.Equal(usersAuditBefore + 1, await CountAsync("SELECT COUNT(*) FROM users_audit WHERE user_id = @u", ("u", anna)));

        Assert.Equal(eventsBefore + 1, await StreamEventCountAsync(anna));
        var payload = await ScalarAsync<string>(
            "SELECT event_payload::text FROM outbox_events WHERE stream_id = @s AND event_type = 'EmployeeProfileCreated' ORDER BY outbox_id DESC LIMIT 1",
            ("s", $"employee-profile-{anna}"));
        Assert.NotNull(payload);
        using (var doc = JsonDocument.Parse(payload!))
        {
            var root = doc.RootElement;
            Assert.Equal("2026-03-25", root.EnumerateObject().First(p => string.Equals(p.Name, "effectiveFrom", StringComparison.OrdinalIgnoreCase)).Value.GetString());
            Assert.Equal(0.8m, root.EnumerateObject().First(p => string.Equals(p.Name, "partTimeFraction", StringComparison.OrdinalIgnoreCase)).Value.GetDecimal());
        }
        Assert.Equal(projectionBefore + 1, await CountAsync("SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @u", ("u", anna)));
        Assert.Equal("TENANT_TARGETED|STY01", await ScalarAsync<string>(
            "SELECT visibility_scope || '|' || target_org_id FROM audit_projection WHERE target_resource_id = @u AND event_type = 'EmployeeProfileCreated'",
            ("u", anna)));
        Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @u", ("u", anna)));

        using var again = await GetAsync(lise, anna);
        var read = await ReadAsync(again, HttpStatusCode.OK, "the GET after the re-create");
        Assert.Equal(0.800m, read.Json.GetProperty("partTimeFraction").GetDecimal());
        Assert.Equal("Fuldmægtig", read.Json.GetProperty("position").GetString());
        Assert.Equal($"\"{after}\"", again.Headers.ETag!.Tag);
    }

    // ═══ AC-Q268b ═══

    /// <summary><b>F (CI).</b> AC-Q268b (1) and consult row 16. No close: a profile covers Friday 10 April 2026. A re-create dated that
    /// day answers 409 with exactly <c>{ "error": "Employee profile already exists", "currentVersion": &lt;users.version&gt; }</c>:
    /// no hint and no date. Nothing written.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428, so the 409 status assertion fails.</para></summary>
    [Fact]
    public async Task Recreate_WhenAProfileCoversToday_409_NothingWritten()
    {
        var host = HostAt(Apr10);
        var anna = await PersonAsync();
        var before = await SnapshotAsync(anna);

        using var rsp = await RecreateAsync(Lise(host), anna, Body("2026-04-10", 0.800m));

        var reply = await ReadAsync(rsp, HttpStatusCode.Conflict, "the re-create over a covering profile");
        AssertMembers(reply.Json, "error", "currentVersion");
        Assert.Equal("Employee profile already exists", reply.Json.GetProperty("error").GetString());
        Assert.Equal(await VersionAsync(anna), reply.Json.GetProperty("currentVersion").GetInt64());
        Assert.DoesNotMatch(new Regex(@"\d{4}-\d{2}-\d{2}"), reply.Text);
        Assert.Equal(before, await SnapshotAsync(anna));
    }

    /// <summary><b>F (CI).</b> AC-Q268b (2) and consult row 17 (the 409 arm). The state "only scheduled" is seeded by SQL, because no route
    /// makes it (a re-create is dated today or earlier): rows <c>[0001-01-01, 2026-03-16)</c> and <c>[2026-12-01, open)</c>. On
    /// Friday 10 April 2026 the GET answers 404 with <c>canCreate</c> false, <c>scheduledFrom</c> "2026-12-01", <c>uncoveredSince</c>
    /// "2026-03-16"; a re-create dated 2026-04-10 answers 409 as above; nothing written.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the GET's 404 body has no members, so the <c>canCreate</c> read fails first;
    /// the re-create would answer 428.</para></summary>
    [Fact]
    public async Task Recreate_WhenAProfileIsScheduled_409_NothingWritten()
    {
        var host = HostAt(Apr10);
        var anna = await PersonAsync();
        await ExecAsync("UPDATE employee_profiles SET effective_to = DATE '2026-03-16' WHERE employee_id = @e", ("e", anna));
        await ExecAsync(
            """
            INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, position, effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @e, 1.000, NULL, DATE '2026-12-01', NULL, 1, (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            """, ("e", anna));
        var lise = Lise(host);

        using (var notFound = await GetAsync(lise, anna))
        {
            var reply = await ReadAsync(notFound, HttpStatusCode.NotFound, "the GET with only a scheduled profile");
            Assert.False(reply.Json.GetProperty("canCreate").GetBoolean());
            Assert.Equal("2026-12-01", reply.Json.GetProperty("scheduledFrom").GetString());
            Assert.Equal("2026-03-16", reply.Json.GetProperty("uncoveredSince").GetString());
        }
        var before = await SnapshotAsync(anna);

        using var rsp = await RecreateAsync(lise, anna, Body("2026-04-10", 0.800m));

        var conflict = await ReadAsync(rsp, HttpStatusCode.Conflict, "the re-create over a scheduled profile");
        AssertMembers(conflict.Json, "error", "currentVersion");
        Assert.Equal("Employee profile already exists", conflict.Json.GetProperty("error").GetString());
        Assert.Equal(before, await SnapshotAsync(anna));
    }

    /// <summary><b>F (CI).</b> Consult row 17 (the success arm): only a zero-length row (a cancelled scheduled change, <c>[f, f)</c>) lies
    /// after today, so no profile is "scheduled" and the re-create goes through the chain. Rows seeded by SQL: <c>[0001-01-01,
    /// 2026-03-16)</c> and <c>[2026-04-20, 2026-04-20)</c>. On Friday 10 April 2026 a re-create dated 2026-04-10 answers 200 and the
    /// rows become <c>[.., 03-16)</c>, <c>[04-10, 04-20)</c> and <c>[04-20, open)</c>.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428. A "scheduled" test that counted the zero-length
    /// row would answer 409 and fail the status assertion.</para></summary>
    [Fact]
    public async Task Recreate_WithOnlyAZeroLengthRowAfterToday_200_ThroughTheChain()
    {
        var host = HostAt(Apr10);
        var anna = await PersonAsync();
        await ExecAsync("UPDATE employee_profiles SET effective_to = DATE '2026-03-16' WHERE employee_id = @e", ("e", anna));
        await ExecAsync(
            """
            INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, position, effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @e, 0.500, NULL, DATE '2026-04-20', DATE '2026-04-20', 1, (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
            """, ("e", anna));

        using var rsp = await RecreateAsync(Lise(host), anna, Body("2026-04-10", 0.800m));

        await ReadAsync(rsp, HttpStatusCode.OK, "the re-create with only a zero-length row after today");
        var rows = await RowsAsync(anna);
        Assert.Equal(3, rows.Count);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)Mar16), (rows[0].From, rows[0].To));
        Assert.Equal((Apr10, (DateOnly?)new DateOnly(2026, 4, 20), 0.800m), (rows[1].From, rows[1].To, rows[1].Fraction));
        Assert.Equal((new DateOnly(2026, 4, 20), (DateOnly?)null, 0.800m), (rows[2].From, rows[2].To, rows[2].Fraction));
    }

    /// <summary><b>R (CI).</b> AC-Q268b (3) and consult rows 13 and 14. After a close at Monday 16 March 2026, an ordinary edit (<c>If-Match</c>)
    /// dated Friday 10 April 2026 answers 404 with exactly <c>{ "error": "Employee profile not found" }</c> and writes nothing: the
    /// state never turns an edit into a create. A second arm sends a stale token (999) and still gets 404, not 412 and not a create.
    /// <para><b>Discriminating assertion.</b> A server that inferred a create from the state would answer 200 and add a row; one that
    /// checked the token before the covering row would answer 412. Green on the base by design (the S33 rule kept).</para></summary>
    [Fact]
    public async Task Edit_WhenNoProfileCoversToday_Still404_NothingWritten()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var anna = await ClosedPersonAsync(h1);
        var lise = Lise(h2);

        foreach (var token in new[] { await VersionAsync(anna), 999L })
        {
            var before = await SnapshotAsync(anna);
            using var rsp = await EditAsync(lise, anna, Body("2026-04-10", 0.800m), token);
            var reply = await ReadAsync(rsp, HttpStatusCode.NotFound, $"the edit with token {token}");
            AssertMembers(reply.Json, "error");
            Assert.Equal("Employee profile not found", reply.Json.GetProperty("error").GetString());
            Assert.Equal(before, await SnapshotAsync(anna));
        }
    }

    // ═══ AC-Q268c ═══

    /// <summary><b>F (CI).</b> AC-Q268c (1), consult row 18. After a close on Monday 16 March 2026, a re-create on Friday 10 April 2026
    /// dated Wednesday 15 April 2026 answers 400, <c>fields</c> exactly one entry <c>EffectiveFrom</c> /
    /// <c>EFFECTIVE_FROM_NOT_TODAY_OR_PAST</c> / nearestValid ["2026-04-10"]; nothing written.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428.</para></summary>
    [Fact]
    public async Task Recreate_FutureDate_400_NothingWritten()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var anna = await ClosedPersonAsync(h1);
        var before = await SnapshotAsync(anna);

        using var rsp = await RecreateAsync(Lise(h2), anna, Body("2026-04-15", 0.800m));

        await AssertValidationAsync(rsp, "EffectiveFrom", "EFFECTIVE_FROM_NOT_TODAY_OR_PAST", new[] { "2026-04-10" }, "the future-dated re-create");
        Assert.Equal(before, await SnapshotAsync(anna));
    }

    /// <summary><b>F (CI).</b> AC-Q268c (2). Dated Friday 13 March 2026, before the last profile's end: 400 <c>EffectiveFrom</c> /
    /// <c>BEFORE_LAST_PROFILE_END</c> / nearestValid ["2026-03-16"]; nothing written.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428.</para></summary>
    [Fact]
    public async Task Recreate_BeforeTheLastProfileEnd_400_NamesIt()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var anna = await ClosedPersonAsync(h1);
        var before = await SnapshotAsync(anna);

        using var rsp = await RecreateAsync(Lise(h2), anna, Body("2026-03-13", 0.800m));

        await AssertValidationAsync(rsp, "EffectiveFrom", "BEFORE_LAST_PROFILE_END", new[] { "2026-03-16" }, "the re-create before the last profile end");
        Assert.Equal(before, await SnapshotAsync(anna));
    }

    /// <summary><b>F (CI).</b> AC-Q268c (3). Dated Monday 16 March 2026, the last profile's own end: 200, and the rows are
    /// <c>[0001-01-01, 2026-03-16)</c> and <c>[2026-03-16, open)</c>: no day uncovered.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428.</para></summary>
    [Fact]
    public async Task Recreate_AtTheLastProfileEnd_LeavesNoGap()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var anna = await ClosedPersonAsync(h1);

        using var rsp = await RecreateAsync(Lise(h2), anna, Body("2026-03-16", 0.800m));

        await ReadAsync(rsp, HttpStatusCode.OK, "the re-create at the last profile end");
        var rows = await RowsAsync(anna);
        Assert.Equal(2, rows.Count);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)Mar16), (rows[0].From, rows[0].To));
        Assert.Equal((Mar16, (DateOnly?)null), (rows[1].From, rows[1].To));
    }

    /// <summary><b>F (CI).</b> AC-Q268c (4), ruling 17 / ruling 4. Anna's employment starts Monday 1 June 2026, after today (Friday 10 April 2026),
    /// and her profile is closed at Monday 16 March 2026: a re-create dated today answers 200 and her new row is
    /// <c>[2026-04-10, open)</c>. <b>Set up by SQL</b> (start date and the closed profile): what the close route does to a person
    /// who has not started is not under test, and SQL reaches the state without depending on it.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428. A floor that refused a date before the start
    /// even for a person who has not started would answer 422.</para></summary>
    [Fact]
    public async Task Recreate_PersonNotYetStarted_CoversToday()
    {
        var host = HostAt(Apr10);
        var anna = await PersonAsync();
        await ExecAsync("UPDATE users SET employment_start_date = DATE '2026-06-01' WHERE user_id = @u", ("u", anna));
        await ExecAsync("UPDATE employee_profiles SET effective_to = DATE '2026-03-16' WHERE employee_id = @e", ("e", anna));

        using var rsp = await RecreateAsync(Lise(host), anna, Body("2026-04-10", 0.800m));

        await ReadAsync(rsp, HttpStatusCode.OK, "the re-create of a person who has not started");
        var rows = await RowsAsync(anna);
        Assert.Equal(2, rows.Count);
        Assert.Equal((Apr10, (DateOnly?)null), (rows[1].From, rows[1].To));
    }

    /// <summary><b>F (CI).</b> AC-Q268c (5), ruling 17 / ruling 4, consult row 21. Closed at Monday 16 March 2026; Anna's employment start
    /// is then set (SQL) to Wednesday 1 April 2026, after the close and on or before today. A re-create dated Wednesday 25 March 2026
    /// answers 422, and its body carries no date: no ISO date and no month name in any member. Nothing written.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428.</para></summary>
    [Fact]
    public async Task Recreate_BeforeEmploymentStart_422_DateFree()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var anna = await ClosedPersonAsync(h1);
        await ExecAsync("UPDATE users SET employment_start_date = DATE '2026-04-01' WHERE user_id = @u", ("u", anna));
        var before = await SnapshotAsync(anna);

        using var rsp = await RecreateAsync(Lise(h2), anna, Body("2026-03-25", 0.800m));

        var reply = await ReadAsync(rsp, HttpStatusCode.UnprocessableEntity, "the re-create before the employment start");
        Assert.DoesNotMatch(new Regex(@"\d{4}-\d{2}-\d{2}"), reply.Text);
        Assert.DoesNotMatch(new Regex(@"(?i)jan|feb|mar|apr|maj|may|jun|jul|aug|sep|okt|oct|nov|dec"), reply.Text);
        Assert.Equal(before, await SnapshotAsync(anna));
    }

    /// <summary><b>R (CI).</b> Consult row 19 (a zero-length row must not move the last profile's end). Two people, each with rows seeded
    /// by SQL: <c>[0001-01-01, 2026-03-16)</c> and a zero-length <c>[2026-04-01, 2026-04-01)</c>. On Friday 10 April 2026 the one dated
    /// Friday 13 March 2026 is refused 400 <c>BEFORE_LAST_PROFILE_END</c> naming 2026-03-16 (not 2026-04-01); the one dated Friday 20 March
    /// 2026 (after the last real end, before the zero-length row) answers 200.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> both re-creates answer 428. A last-end read that counted the zero-length row
    /// would name 2026-04-01 and refuse 20 March with 400.</para></summary>
    [Fact]
    public async Task Recreate_ZeroLengthRowDoesNotMoveTheLastProfileEnd()
    {
        var host = HostAt(Apr10);
        var lise = Lise(host);
        var refused = await PersonAsync();
        var allowed = await PersonAsync();
        foreach (var anna in new[] { refused, allowed })
        {
            await ExecAsync("UPDATE employee_profiles SET effective_to = DATE '2026-03-16' WHERE employee_id = @e", ("e", anna));
            await ExecAsync(
                """
                INSERT INTO employee_profiles (profile_id, employee_id, part_time_fraction, position, effective_from, effective_to, version, employment_category)
                VALUES (gen_random_uuid(), @e, 0.500, NULL, DATE '2026-04-01', DATE '2026-04-01', 1, (SELECT u.employment_category FROM users u WHERE u.user_id = @e))
                """, ("e", anna));
        }
        var before = await SnapshotAsync(refused);

        using (var rsp = await RecreateAsync(lise, refused, Body("2026-03-13", 0.800m)))
            await AssertValidationAsync(rsp, "EffectiveFrom", "BEFORE_LAST_PROFILE_END", new[] { "2026-03-16" }, "the re-create before L");
        Assert.Equal(before, await SnapshotAsync(refused));

        using var ok = await RecreateAsync(lise, allowed, Body("2026-03-20", 0.800m));
        await ReadAsync(ok, HttpStatusCode.OK, "the re-create between L and the zero-length row");
    }

    // ═══ AC-Q268d: nobody re-creates their own profile ═══

    /// <summary><b>F (CI).</b> AC-Q268d (1). The person is the actor and her profile was closed first by a GlobalAdmin who is not her.
    /// Arms, each 403 exactly <c>{ "error": "Access denied", "reason": &lt;the P-26 reason&gt; }</c> with nothing written: (1) a LocalHR
    /// self; (2) a GlobalAdmin self; (3) a LocalHR self sending fraction 0 (403, not 400: the refusal comes before the values).
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> each answers 428.</para></summary>
    [Fact]
    public async Task Recreate_OwnProfile_403_NothingWritten()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var arms = new (string Name, Func<string, HttpClient> Client, decimal Fraction)[]
        {
            ("a LocalHR self", id => LocalHrClient(h2, id, "STY01"), 0.800m),
            ("a GlobalAdmin self", id => GlobalAdminClient(h2, id), 0.800m),
            ("a LocalHR self with fraction 0", id => LocalHrClient(h2, id, "STY01"), 0m),
        };
        foreach (var (name, client, fraction) in arms)
        {
            var self = await PersonAsync();
            await CloseAsync(GlobalAdminClient(h1), self);
            var before = await SnapshotAsync(self);

            using var rsp = await RecreateAsync(client(self), self, Body("2026-04-10", fraction));

            await AssertForbiddenAsync(rsp, SelfReason, name);
            Assert.Equal(before, await SnapshotAsync(self));
        }
    }

    /// <summary><b>F (CI).</b> Consult row 3. A LocalHR self sends a re-create that is also invalid or impossible, and still gets the self
    /// refusal, never 400 and never 409, and the 403 body carries no <c>currentVersion</c>: (1) dated Wednesday 15 April 2026 (after
    /// today, Friday 10 April); (2) fraction 0; (3) a person whose profile still covers today (the 409 case). Nothing written.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> each answers 428. A self check placed after the values or the probe would answer
    /// 400 or 409.</para></summary>
    [Fact]
    public async Task Recreate_Self_RefusedBeforeValuesAndBeforeTheProbe_403_Never400Or409()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var arms = new (string Name, bool Close, string From, decimal Fraction)[]
        {
            ("a future date", true, "2026-04-15", 0.800m),
            ("fraction 0", true, "2026-04-10", 0m),
            ("a profile that already exists", false, "2026-04-10", 0.800m),
        };
        foreach (var (name, close, from, fraction) in arms)
        {
            var self = await PersonAsync();
            if (close)
                await CloseAsync(GlobalAdminClient(h1), self);
            var before = await SnapshotAsync(self);

            using var rsp = await RecreateAsync(LocalHrClient(h2, self, "STY01"), self, Body(from, fraction));

            await AssertForbiddenAsync(rsp, SelfReason, name);
            Assert.Equal(before, await SnapshotAsync(self));
        }
    }

    /// <summary><b>R (CI).</b> Consult rows 4 and 15. Three bad precondition shapes each answer 428 and write nothing, for a self actor
    /// (not 403: the header is read before the self check) and for Lise on another person: no precondition header; both
    /// <c>If-Match: "1"</c> and <c>If-None-Match: *</c>; <c>If-None-Match: "1"</c>.
    /// <para><b>Discriminating assertion.</b> The 428 for the self actor, which a self check placed before the header parse would turn
    /// into 403. Green on the base (it answers 428 to all of these) by design.</para></summary>
    [Fact]
    public async Task Recreate_BadPrecondition_428_ForSelfAndForAnotherPerson()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var shapes = new (string Name, (string, string)[] Headers)[]
        {
            ("no precondition", new[] { ("X-S148-Marker", "none") }),
            ("both headers", new[] { ("If-Match", "\"1\""), ("If-None-Match", "*") }),
            ("If-None-Match with a version", new[] { ("If-None-Match", "\"1\"") }),
        };
        var self = await PersonAsync();
        var other = await PersonAsync();
        await CloseAsync(GlobalAdminClient(h1), self);
        await CloseAsync(GlobalAdminClient(h1), other);
        foreach (var (name, headers) in shapes)
        {
            foreach (var (person, client) in new[] { (self, LocalHrClient(h2, self, "STY01")), (other, Lise(h2)) })
            {
                var before = await SnapshotAsync(person);
                using var rsp = await RecreateAsync(client, person, Body("2026-04-10", 0.800m), headers);
                var reply = await ReadAsync(rsp, (HttpStatusCode)428, $"{name} for {(person == self ? "the self actor" : "another person")}");
                AssertMembers(reply.Json, "error");
                Assert.Equal(before, await SnapshotAsync(person));
            }
        }
    }

    /// <summary><b>R (CI).</b> AC-Q268d (2) and consult row 6. A departed person acting on their own record: her last day was Wednesday 4 March 2026
    /// (a LocalHR of STY01, the clock Friday 10 April 2026), and the arms are one still active and one deactivated. The re-create and the
    /// GET each answer 403 whose reason is exactly <c>A departed employee cannot act on their own record</c>, not the self reason; nothing
    /// written.
    /// <para><b>Discriminating assertion.</b> The reason string. Green on the base (the scope check runs first there too).</para></summary>
    [Fact]
    public async Task Recreate_DepartedSelf_TheScopeCheckAnswersFirst()
    {
        var host = HostAt(Apr10);
        foreach (var active in new[] { true, false })
        {
            var self = await PersonAsync();
            await ExecAsync(
                "UPDATE users SET employment_end_date = DATE '2026-03-04', is_active = @a, end_date_deactivated = @d WHERE user_id = @u",
                ("a", active), ("d", !active), ("u", self));
            var before = await SnapshotAsync(self);
            var client = LocalHrClient(host, self, "STY01");

            using (var put = await RecreateAsync(client, self, Body("2026-04-10", 0.800m)))
                await AssertForbiddenAsync(put, DepartedReason, $"the departed self's re-create (active: {active})");
            using (var get = await GetAsync(client, self))
                await AssertForbiddenAsync(get, DepartedReason, $"the departed self's GET (active: {active})");
            Assert.Equal(before, await SnapshotAsync(self));
        }
    }

    /// <summary><b>F (CI).</b> Consult row 5. The actor acts on herself with a token that is a LocalHR of STY02 and a LocalLeader of STY01
    /// (her own organisation): the scope check floors each scope at LocalHR, so the Leader scope never admits, and the answer is the
    /// validator's own 403 <c>Actor scope does not cover target organization</c>, not the self reason. The two-role token is the
    /// reachable form of "only a scope below LocalHR": a Leader-role token is stopped by the policy before the handler. Nothing written.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 403 with the validator's reason too, so this fact
    /// is R on the base for the PUT; it fails if the self check were placed before the scope check (it would answer the self
    /// reason).</para></summary>
    [Fact]
    public async Task Recreate_SelfWithOnlyALeaderScope_GetsTheValidatorsReason_NotTheSelfReason()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var self = await ClosedPersonAsync(h1);
        var before = await SnapshotAsync(self);
        var client = TokenClient(h2, self, StatsTidRoles.LocalHR,
            new RoleScope(StatsTidRoles.LocalHR, "STY02", "ORG_ONLY"), new RoleScope(StatsTidRoles.LocalLeader, "STY01", "ORG_ONLY"));

        using var rsp = await RecreateAsync(client, self, Body("2026-04-10", 0.800m));

        await AssertForbiddenAsync(rsp, NotCoveredReason, "the self re-create with a Leader-only cover");
        Assert.Equal(before, await SnapshotAsync(self));
    }

    // ═══ the scope check (consult rows 7-11) ═══

    /// <summary><b>R (CI).</b> Consult rows 7 and 8. A LocalHR of STY02 only, and a token that is a LocalHR of STY02 plus a LocalLeader of STY01,
    /// each act on a person of STY01 whose profile is closed: the re-create and the GET answer 403 <c>Actor scope does not cover target
    /// organization</c>; the GET body carries no 404 members. Nothing written.
    /// <para><b>Discriminating assertion.</b> The reason and the absence of <c>canCreate</c> in the GET body: a 404 body for an out-of-scope
    /// person would be an oracle. Green on the base for the PUT (the scope check is unchanged); the GET half is the new body's guard.</para></summary>
    [Fact]
    public async Task RecreateAndGet_ActorWithoutHrCoverOfTheOrganisation_403_NoBody()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var anna = await ClosedPersonAsync(h1);
        var before = await SnapshotAsync(anna);
        var actors = new (string Name, HttpClient Client)[]
        {
            ("a LocalHR of STY02", LocalHrClient(h2, "HR_S148_Q268_O", "STY02")),
            ("a LocalHR of STY02 plus a Leader of STY01", TokenClient(h2, "HR_S148_Q268_M", StatsTidRoles.LocalHR,
                new RoleScope(StatsTidRoles.LocalHR, "STY02", "ORG_ONLY"), new RoleScope(StatsTidRoles.LocalLeader, "STY01", "ORG_ONLY"))),
        };
        foreach (var (name, client) in actors)
        {
            using (var put = await RecreateAsync(client, anna, Body("2026-04-10", 0.800m)))
                await AssertForbiddenAsync(put, NotCoveredReason, $"{name}: the re-create");
            using (var get = await GetAsync(client, anna))
            {
                await AssertForbiddenAsync(get, NotCoveredReason, $"{name}: the GET");
                Assert.DoesNotContain("canCreate", await get.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            Assert.Equal(before, await SnapshotAsync(anna));
        }
    }

    /// <summary><b>R (CI).</b> Consult row 9. A LocalHR of the parent organisation MIN01 acts on people of its child STY01: with an
    /// <c>ORG_ONLY</c> scope (the flat scope since S93, which covers exactly its own organisation) the re-create, the GET and the ordinary
    /// edit all answer 403 <c>Actor scope does not cover target organization</c>; with a stale <c>ORG_AND_DESCENDANTS</c> scope the create's
    /// and the GET's answers equal the edit's (status and reason). The create reaches exactly whom the edit reaches.
    /// <para><b>Discriminating assertion.</b> Parity of the three routes. Green on the base for the PUT (scope unchanged).</para></summary>
    [Fact]
    public async Task Recreate_ParentOrganisationHr_ReachesExactlyWhomTheEditReaches()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var closed = await ClosedPersonAsync(h1);
        var covered = await PersonAsync();
        foreach (var scopeType in new[] { "ORG_ONLY", "ORG_AND_DESCENDANTS" })
        {
            var client = TokenClient(h2, "HR_S148_Q268_P", StatsTidRoles.LocalHR, new RoleScope(StatsTidRoles.LocalHR, "MIN01", scopeType));
            var before = await SnapshotAsync(closed);

            using var create = await RecreateAsync(client, closed, Body("2026-04-10", 0.800m));
            using var read = await GetAsync(client, closed);
            using var edit = await EditAsync(client, covered, Body("2026-04-10", 0.800m), await VersionAsync(covered));

            var createReply = await ParseAnyAsync(create);
            var readReply = await ParseAnyAsync(read);
            var editReply = await ParseAnyAsync(edit);
            Assert.Equal(editReply, createReply);
            Assert.Equal(editReply, readReply);
            if (scopeType == "ORG_ONLY")
                Assert.Equal((403, NotCoveredReason), createReply);
            Assert.Equal(before, await SnapshotAsync(closed));
        }
    }

    private static async Task<(int Status, string? Reason)> ParseAnyAsync(HttpResponseMessage rsp)
    {
        var text = await rsp.Content.ReadAsStringAsync();
        string? reason = null;
        if (text.Length > 0 && rsp.StatusCode == HttpStatusCode.Forbidden)
            reason = JsonDocument.Parse(text).RootElement.GetProperty("reason").GetString();
        return ((int)rsp.StatusCode, reason);
    }

    /// <summary><b>R (CI).</b> Consult row 10. The target is deactivated (a leaver, closed first), or active but past the manager grace (last
    /// day Tuesday 31 March 2026, the clock Friday 10 April 2026), and the actor holds only a Leader scope over the target's organisation
    /// next to an HR scope elsewhere (the two-role token): the re-create and the GET answer 403 <c>Actor scope does not cover target
    /// organization</c>. Nothing written.
    /// <para><b>Discriminating assertion.</b> The refusal itself: a terminated person is reached only through a scope that is itself LocalHR
    /// or above. Green on the base for the PUT (the scope check is unchanged).</para></summary>
    [Fact]
    public async Task RecreateAndGet_TerminatedTarget_BelowHrScopeOnly_403()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var deactivated = await ClosedPersonAsync(h1);
        await ExecAsync(
            "UPDATE users SET employment_end_date = DATE '2026-03-04', is_active = FALSE, end_date_deactivated = TRUE WHERE user_id = @u",
            ("u", deactivated));
        var pastGrace = await ClosedPersonAsync(h1);
        await ExecAsync("UPDATE users SET employment_end_date = DATE '2026-03-31' WHERE user_id = @u", ("u", pastGrace));
        var client = TokenClient(h2, "HR_S148_Q268_M", StatsTidRoles.LocalHR,
            new RoleScope(StatsTidRoles.LocalHR, "STY02", "ORG_ONLY"), new RoleScope(StatsTidRoles.LocalLeader, "STY01", "ORG_ONLY"));

        foreach (var person in new[] { deactivated, pastGrace })
        {
            var before = await SnapshotAsync(person);
            using (var put = await RecreateAsync(client, person, Body("2026-04-10", 0.800m)))
                await AssertForbiddenAsync(put, NotCoveredReason, "the re-create of a terminated target");
            using (var get = await GetAsync(client, person))
                await AssertForbiddenAsync(get, NotCoveredReason, "the GET of a terminated target");
            Assert.Equal(before, await SnapshotAsync(person));
        }
    }

    /// <summary><b>R (CI).</b> Consult row 11. An Employee token and a LocalLeader token (each with a scope over STY01) are stopped at the
    /// <c>HROrAbove</c> policy: the re-create and the GET answer 403 and nothing is written.
    /// <para><b>Discriminating assertion.</b> The 403 status for both roles on both routes. Green on the base (the policy is unchanged).</para></summary>
    [Fact]
    public async Task RecreateAndGet_EmployeeAndLeaderTokens_403AtThePolicy()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var anna = await ClosedPersonAsync(h1);
        var before = await SnapshotAsync(anna);
        foreach (var role in new[] { StatsTidRoles.Employee, StatsTidRoles.LocalLeader })
        {
            var client = TokenClient(h2, "S148_Q268_" + role, role, new RoleScope(role, "STY01", "ORG_ONLY"));
            using var put = await RecreateAsync(client, anna, Body("2026-04-10", 0.800m));
            Assert.True(put.StatusCode == HttpStatusCode.Forbidden, $"{role}: the re-create answered {(int)put.StatusCode}");
            using var get = await GetAsync(client, anna);
            Assert.True(get.StatusCode == HttpStatusCode.Forbidden, $"{role}: the GET answered {(int)get.StatusCode}");
            Assert.Equal(before, await SnapshotAsync(anna));
        }
    }

    // ═══ AC-Q268e / consult row 12: the drift race ═══

    /// <summary><b>F (CI).</b> AC-Q268e and consult row 12 (the AC-C13n choreography, copied in shape from <c>OrganisationDriftGuardTests</c>).
    /// Friday 10 April 2026 is the one clock (the close and the re-create share a host). A side connection locks the person's users row
    /// (<c>FOR UPDATE</c>); the re-create (dated 2026-04-10, Lise of STY01 only) is sent unawaited; the test polls <c>pg_stat_activity</c>
    /// until a backend waits on a lock (it gives up when the request completes; a timeout fails the fact); the side connection sets
    /// <c>primary_org_id = 'STY02'</c>, <c>version = version + 1</c> and commits. The re-create answers 409 exactly
    /// <c>{ "error": "The user's organisation changed while this request was being processed; refresh and retry." }</c>. Two people: Anna,
    /// and Berit, whose close retired a cancelled scheduled change (a zero-length row <c>[2026-04-20, 2026-04-20)</c>, so the create would
    /// chain). Nothing written beyond the close: no new row, no CREATED or UPDATED audit row, no chained write (no audit row with a
    /// ADMIN_PROFILE_CREATE source), no event, no projection row, no worklist row.
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428 before any lock, so the wait is never observed and the
    /// fact fails at "never blocked on a lock".</para></summary>
    [Fact]
    public async Task Recreate_OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var host = HostAt(Apr10);
        var lise = Lise(host);

        var anna = await PersonAsync();
        await CloseAsync(lise, anna);

        var berit = await PersonAsync();
        using (var scheduled = await EditAsync(lise, berit, Body("2026-04-20", 0.500m), await VersionAsync(berit)))
            await ReadAsync(scheduled, HttpStatusCode.OK, "the edit that schedules a change");
        await CloseAsync(lise, berit);
        Assert.Contains(await RowsAsync(berit), r => r.From == new DateOnly(2026, 4, 20) && r.To == new DateOnly(2026, 4, 20));

        foreach (var person in new[] { anna, berit })
        {
            var versionBefore = await VersionAsync(person);
            var trailBefore = await SelfChangeRefusalTests.WriteTrailAsync(ConnectionString, person);

            await using var side = new NpgsqlConnection(ConnectionString);
            await side.OpenAsync();
            await using var sideTx = await side.BeginTransactionAsync();
            await using (var lockCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE user_id = @u FOR UPDATE", side, sideTx))
            {
                lockCmd.Parameters.AddWithValue("u", person);
                await lockCmd.ExecuteScalarAsync();
            }

            var request = RecreateAsync(lise, person, Body("2026-04-10", 0.800m));
            var observed = await Payroll.PriorMonthExportRaceTests.WaitUntilAsync(async () =>
            {
                await using var probe = new NpgsqlConnection(ConnectionString);
                await probe.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid <> pg_backend_pid() AND datname = current_database() AND wait_event_type = 'Lock')",
                    probe);
                return await cmd.ExecuteScalarAsync() is true;
            }, TimeSpan.FromSeconds(30), () => request.IsCompleted);
            Assert.True(observed, $"the re-create never blocked on a lock within the timeout (request status: {request.Status}).");
            Assert.False(request.IsCompleted, "the re-create finished before the side transaction committed.");

            await using (var move = new NpgsqlCommand(
                "UPDATE users SET primary_org_id = 'STY02', version = version + 1 WHERE user_id = @u", side, sideTx))
            {
                move.Parameters.AddWithValue("u", person);
                Assert.Equal(1, await move.ExecuteNonQueryAsync());
            }
            await sideTx.CommitAsync();

            using var rsp = await request;
            var reply = await ReadAsync(rsp, HttpStatusCode.Conflict, "the re-create after the move");
            AssertMembers(reply.Json, "error");
            Assert.Equal(DriftError, reply.Json.GetProperty("error").GetString());
            Assert.Equal("STY02", await UsersPrimaryOrgAsync(ConnectionString, person));
            Assert.Equal(versionBefore + 1, await VersionAsync(person));
            Assert.Equal(trailBefore, await SelfChangeRefusalTests.WriteTrailAsync(ConnectionString, person));
            Assert.Equal(0L, await CountAsync(
                "SELECT COUNT(*) FROM employee_profile_audit WHERE employee_id = @e AND new_data->>'source' LIKE 'ADMIN_PROFILE_CREATE%'", ("e", person)));
            Assert.Equal(0L, await CountAsync(
                "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type IN ('EmployeeProfileCreated', 'EmployeeProfileUpdated')",
                ("s", $"employee-profile-{person}")));
            Assert.Equal(0L, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @e", ("e", person)));
        }
    }

    // ═══ AC-Q268i / consult row 22: across a retired scheduled change ═══

    /// <summary>One person whose close retired a scheduled change: at Monday 16 March 2026 an edit dated 1 April with fraction 0.500 and the
    /// category "Chefkonsulent", then the close; March 2026 is exported (so the worklist can show triggers).</summary>
    private async Task<(string Id, string FirstCategory)> RetiredChangePersonAsync(WebApplicationFactory<Program> closeHost)
    {
        var lise = Lise(closeHost);
        var anna = await PersonAsync();
        var firstCategory = (await RowsAsync(anna))[0].Category;
        using (var edit = await EditAsync(lise, anna, Body("2026-04-01", 0.500m, null, "Chefkonsulent"), await VersionAsync(anna)))
            await ReadAsync(edit, HttpStatusCode.OK, "the edit that schedules the change");
        await ExecAsync(
            """
            INSERT INTO payroll_export_records (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            VALUES (gen_random_uuid(), NULL, @e, 2026, 3, '[]'::jsonb, '[]'::jsonb, 'hash-2026-3')
            """, ("e", anna));
        await CloseAsync(lise, anna);
        return (anna, firstCategory);
    }

    /// <summary><b>F (CI).</b> AC-Q268i (1) and consult row 22. After the retired-change setup at Monday 16 March 2026, on Friday 20 March 2026 the
    /// GET says <c>canCreate</c> true and <c>uncoveredSince</c> "2026-03-16". A re-create dated 16 March, fraction 0.800, sent with
    /// <c>carryForwardToScheduledChange: true</c>, answers 200. Rows: <c>[2026-03-16, 2026-04-01)</c> new and <c>[2026-04-01, open)</c> on the
    /// retired row's <c>profile_id</c>, both 0.800 and the first row's category (not "Chefkonsulent"). New audit rows: <c>CREATED</c> with
    /// source ADMIN_PROFILE_CREATE, then <c>UPDATED</c> with source ADMIN_PROFILE_CREATE_REOPEN, versions chained before, +1, +2; the ETag is
    /// before + 2; no other source appears (no carry-forward write). No worklist trigger of kind EMPLOYMENT_CATEGORY_CHANGE.
    /// <para><b>Product code between the readings:</b> the edit's scheduled split, the close's retirement, the GET's 404 read, the create
    /// branch, the chain (writer and routed write twice).</para>
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the GET's 404 body has no <c>canCreate</c> (first failing assertion) and the re-create
    /// answers 428.</para></summary>
    [Fact]
    public async Task Recreate_AcrossARetiredScheduledChange_RunsOpenFromTheChosenDate()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Mar20);
        var (anna, firstCategory) = await RetiredChangePersonAsync(h1);
        var retiredId = (await RowsAsync(anna)).Single(r => r.From == new DateOnly(2026, 4, 1) && r.To == new DateOnly(2026, 4, 1)).Id;
        var lise = Lise(h2);

        using (var notFound = await GetAsync(lise, anna))
        {
            var reply = await ReadAsync(notFound, HttpStatusCode.NotFound, "the GET after the close");
            Assert.True(reply.Json.GetProperty("canCreate").GetBoolean());
            Assert.Equal("2026-03-16", reply.Json.GetProperty("uncoveredSince").GetString());
        }
        var before = await VersionAsync(anna);
        var auditMark = await MaxAuditIdAsync(anna);

        using var created = await RecreateAsync(lise, anna, Body("2026-03-16", 0.800m, null, null, carry: true));

        await ReadAsync(created, HttpStatusCode.OK, "the re-create across the retired change");
        Assert.Equal($"\"{before + 2}\"", created.Headers.ETag!.Tag);
        var rows = await RowsAsync(anna);
        Assert.Equal(3, rows.Count);
        Assert.Equal((Mar16, (DateOnly?)new DateOnly(2026, 4, 1), 0.800m, firstCategory), (rows[1].From, rows[1].To, rows[1].Fraction, rows[1].Category));
        Assert.Equal((retiredId, new DateOnly(2026, 4, 1), (DateOnly?)null, 0.800m, firstCategory),
            (rows[2].Id, rows[2].From, rows[2].To, rows[2].Fraction, rows[2].Category));

        var audit = await AuditRowsAsync(anna, auditMark);
        Assert.Equal(2, audit.Count);
        Assert.Equal(("CREATED", "ADMIN_PROFILE_CREATE", before, before + 1),
            (audit[0].Action, Prop(audit[0].NewData, "source").GetString(), audit[0].Before, audit[0].After));
        Assert.Equal(("UPDATED", "ADMIN_PROFILE_CREATE_REOPEN", before + 1, before + 2),
            (audit[1].Action, Prop(audit[1].NewData, "source").GetString(), audit[1].Before, audit[1].After));

        var triggerKinds = new List<string>();
        foreach (var json in await ReadTriggersAsync(anna))
            triggerKinds.AddRange(WorklistTriggerJson.Deserialize(json).Select(t => t.Kind));
        Assert.DoesNotContain("EMPLOYMENT_CATEGORY_CHANGE", triggerKinds);
    }

    /// <summary><b>F (CI).</b> AC-Q268i (2). Same setup (closed at Monday 16 March 2026 with the retired change dated 1 April). On Friday 10 April
    /// 2026 a re-create dated Monday 6 April 2026 gives a new open row <c>[2026-04-06, open)</c> holding the first row's category (not the
    /// cancelled change's "Chefkonsulent"), and the zero-length row is untouched.
    /// <para><b>Product code between the readings:</b> the create branch, the writer and routed write once (6 April is after the zero-length
    /// row's date, so there is no chain).</para>
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-create answers 428.</para></summary>
    [Fact]
    public async Task Recreate_AfterARetiredChangesDate_KeepsTheLastRealCategory()
    {
        var h1 = HostAt(Mar16);
        var h2 = HostAt(Apr10);
        var (anna, firstCategory) = await RetiredChangePersonAsync(h1);
        var retiredBefore = (await RowsAsync(anna)).Single(r => r.From == new DateOnly(2026, 4, 1) && r.To == new DateOnly(2026, 4, 1));

        using var created = await RecreateAsync(Lise(h2), anna, Body("2026-04-06", 0.800m));

        await ReadAsync(created, HttpStatusCode.OK, "the re-create dated after the retired change");
        var rows = await RowsAsync(anna);
        Assert.Equal(3, rows.Count);
        Assert.Equal(retiredBefore, rows.Single(r => r.Id == retiredBefore.Id));
        var created6 = rows.Single(r => r.From == new DateOnly(2026, 4, 6));
        Assert.Equal((new DateOnly(2026, 4, 6), (DateOnly?)null, 0.800m, firstCategory), (created6.From, created6.To, created6.Fraction, created6.Category));
    }

    private async Task<List<string>> ReadTriggersAsync(string id)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT triggers::text FROM hr_backdate_worklist WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", id);
        var all = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            all.Add(reader.GetString(0));
        return all;
    }

    // ═══ AC-Q268j / consult row 20: the fraction rule ═══

    /// <summary><b>F (CI).</b> AC-Q268j and consult row 20 (ruling 6, ruling 17). Each arm answers 400 exactly <c>{ "error": "Employee profile
    /// validation failed", "fields": [ { "field": "PartTimeFraction", "code": &lt;c&gt;, "nearestValid": null } ] }</c> and writes nothing, never
    /// a 500: re-create (after a close, Friday 10 April 2026) with 0 and -1 (<c>MUST_BE_POSITIVE</c>), 0.1234 (<c>TOO_MANY_DECIMALS</c>), 10 and
    /// 10000 (<c>EXCEEDS_STORAGE_LIMIT</c>); edit (<c>If-Match</c>, on a covered person) with -0.5 and 0 (<c>MUST_BE_POSITIVE</c>), 0.1234
    /// (<c>TOO_MANY_DECIMALS</c>), 10 (<c>EXCEEDS_STORAGE_LIMIT</c>). There is no 1.000 arm: a fraction above 1.000 is accepted
    /// (<c>EmployeeProfileEndpointTests</c> pins 1.5).
    /// <para><b>Red conditions (F).</b> On <c>319f8b68</c> the re-creates answer 428, the edit with -0.5 and 0 answers 200, the edit with 0.1234
    /// is rounded and answers 200, and the edit with 10 answers 500 (the column's overflow); the first of those assertions fails.</para></summary>
    [Fact]
    public async Task RecreateAndEdit_FractionOutOfRange_400_NothingWritten()
    {
        var host = HostAt(Apr10);
        var lise = Lise(host);
        var closed = await PersonAsync();
        await CloseAsync(lise, closed);
        var covered = await PersonAsync();

        foreach (var (fraction, code) in new[]
        {
            (0m, "MUST_BE_POSITIVE"), (-1m, "MUST_BE_POSITIVE"), (0.1234m, "TOO_MANY_DECIMALS"),
            (10m, "EXCEEDS_STORAGE_LIMIT"), (10000m, "EXCEEDS_STORAGE_LIMIT"),
        })
        {
            var before = await SnapshotAsync(closed);
            using var rsp = await RecreateAsync(lise, closed, Body("2026-04-10", fraction));
            await AssertValidationAsync(rsp, "PartTimeFraction", code, null, $"the re-create with {fraction}");
            Assert.Equal(before, await SnapshotAsync(closed));
        }

        foreach (var (fraction, code) in new[]
        {
            (-0.5m, "MUST_BE_POSITIVE"), (0m, "MUST_BE_POSITIVE"), (0.1234m, "TOO_MANY_DECIMALS"), (10m, "EXCEEDS_STORAGE_LIMIT"),
        })
        {
            var before = await SnapshotAsync(covered);
            using var rsp = await EditAsync(lise, covered, Body("2026-04-10", fraction), await VersionAsync(covered));
            await AssertValidationAsync(rsp, "PartTimeFraction", code, null, $"the edit with {fraction}");
            Assert.Equal(before, await SnapshotAsync(covered));
        }
    }
}
