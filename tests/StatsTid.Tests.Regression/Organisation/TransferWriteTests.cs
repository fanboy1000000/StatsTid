using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Organisation;

/// <summary>
/// S148 / TASK-14820-T (AC-C13d) — the dated organisation's WRITE: a transfer's two write shapes
/// (close-and-open, same-day in place), its refusals, and the rule that the refusals fire only when
/// the organisation actually changes (ADR-040 D4, C-13).
///
/// <para>
/// <b>What this suite is about, in plain language.</b> A person now has a dated history of which
/// institution they belonged to. HR moves someone with a date: a move dated after the start of the
/// current row closes that row and opens a new one; a second move on the same date changes the row in
/// place; a move with no date means today. A date in the future, or before the start of the current
/// row, is refused with a 400 that names the nearest valid day. HR's edit drawer sends the person's
/// organisation on every save, so a save that leaves the organisation unchanged must not meet those
/// refusals (it can still schedule or correct an agreement code).
/// </para>
///
/// <para>
/// <b>Fixture.</b> A Backend host with the clock fixed at Tuesday 10 November 2026 (PAT-008). The
/// acting GlobalAdmin's token carries employee id <c>ADMIN_S148_ORG</c>, no home organisation and a
/// GLOBAL scope, so its own organisation is none of STY01, STY02 or STY03 (the projection facts rely
/// on that). The people transferred are made by the user create or by <see cref="RegressionSeed"/>,
/// never by the backfill seeder (M-41's frozen list relies on it). Organisation rows are read back
/// through Npgsql as <see cref="DateOnly"/> and compared in C#, whichever storage the date has.
/// </para>
///
/// <para>
/// <b>Shared helpers.</b> The <c>internal static</c> members below are reused by the other host
/// classes of this folder (one fixture, no second copy). Docker is unavailable on the authoring
/// machine: these facts are compiler-validated locally and CI-pending until CI-W4.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TransferWriteTests : IAsyncLifetime
{
    /// <summary>The fixed clock: Tuesday 10 November 2026.</summary>
    internal static readonly DateOnly Clock = new(2026, 11, 10);

    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // PAT-008 boot order: the fixed host boots first; every fixture is seeded after it.
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

    // ═════════════════════════════════════════════════════════════════════
    // The write shapes
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F.</b> STY01 → STY02 dated Wednesday 11 November 2026 (the day after the clock) is refused:
    /// 400, one <c>fields</c> shape, nothing written.
    /// <para><b>Red conditions.</b> On the code before C-13 a transfer has no date rule, so the request
    /// answers 200: <c>Assert.Equal(BadRequest, status)</c> fails (and the organisation table does not
    /// exist as a writer's concern, so no row assertion could hold either).</para>
    /// </summary>
    [Fact]
    public async Task FutureDate_400_NearestValidToday()
    {
        var userId = await SeedPersonAsync(_harness.ConnectionString, "STY01");
        var client = GlobalAdminClient(_host);
        var before = await SnapshotAsync(_harness.ConnectionString, userId);

        var rsp = await PutUserAsync(client, _harness.ConnectionString, userId,
            new { primaryOrgId = "STY02", effectiveFrom = "2026-11-11" });

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        await AssertDateRefusalAsync(rsp, "EFFECTIVE_FROM_NOT_TODAY_OR_PAST", "2026-11-10");
        Assert.Equal(before, await SnapshotAsync(_harness.ConnectionString, userId));
    }

    /// <summary>
    /// <b>F.</b> A transfer with no date and no agreement code means today: the old row is closed at
    /// 10 November and the new one starts that day.
    /// <para><b>Red conditions.</b> Before C-13 a transfer writes no organisation row at all, so
    /// <c>ReadOrgRowsAsync</c> returns the single row the fixture seeded (STY01, open) and the
    /// two-row <c>Assert.Equal</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task OmittedDate_WritesToday()
    {
        var userId = await SeedPersonAsync(_harness.ConnectionString, "STY01");
        var client = GlobalAdminClient(_host);

        var rsp = await PutUserAsync(client, _harness.ConnectionString, userId, new { primaryOrgId = "STY02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadOrgRowsAsync(_harness.ConnectionString, userId);
        Assert.Equal(2, rows.Count);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)Clock, "STY01"), (rows[0].From, rows[0].To, rows[0].OrgId));
        Assert.Equal((Clock, (DateOnly?)null, "STY02"), (rows[1].From, rows[1].To, rows[1].OrgId));
    }

    /// <summary>
    /// <b>F.</b> STY01 → STY02 dated Monday 2 November 2026: the live row is closed at 2 November and a
    /// new one opened with the version one higher; one SUPERSEDED audit row; one event, not in place,
    /// naming the closed row, dated 2 November; and the projection row targets the NEW organisation.
    /// <para><b>Red conditions.</b> Before C-13 no organisation row, audit row or
    /// <c>UserOrganisationTransferred</c> event is written: the row-count, the audit-count and the
    /// event-count assertions each fail. The projection assertion is red if the row targets the old
    /// organisation or the actor's.</para>
    /// </summary>
    [Fact]
    public async Task LaterDate_ClosesOneOpensOne_OneAuditRowOneEvent()
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01");
        var client = GlobalAdminClient(_host);
        var seededRows = await ReadOrgRowsAsync(cs, userId);
        var closedId = Assert.Single(seededRows).AssignmentId;

        var rsp = await PutUserAsync(client, cs, userId,
            new { primaryOrgId = "STY02", effectiveFrom = "2026-11-02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadOrgRowsAsync(cs, userId);
        Assert.Equal(2, rows.Count);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)new DateOnly(2026, 11, 2), "STY01", 1L),
            (rows[0].From, rows[0].To, rows[0].OrgId, rows[0].Version));
        Assert.Equal((new DateOnly(2026, 11, 2), (DateOnly?)null, "STY02", 2L),
            (rows[1].From, rows[1].To, rows[1].OrgId, rows[1].Version));

        var audit = await ReadOrgAuditAsync(cs, userId);
        var auditRow = Assert.Single(audit);
        Assert.Equal("SUPERSEDED", auditRow.Action);
        Assert.Equal(rows[1].AssignmentId, auditRow.AssignmentId);

        var events = await ReadEventsAsync(cs, userId, "UserOrganisationTransferred");
        var evt = Assert.Single(events);
        Assert.False(evt.Payload.GetProperty("inPlace").GetBoolean());
        Assert.Equal(closedId, evt.Payload.GetProperty("predecessorAssignmentId").GetGuid());
        Assert.Equal("2026-11-02", evt.Payload.GetProperty("effectiveFrom").GetString());

        // The projection row targets the NEW organisation (never the old one, never the actor's).
        Assert.Equal("STY02", await ProjectionTargetOrgAsync(cs, evt.EventId));
        Assert.Equal(userId, await ProjectionTargetResourceAsync(cs, evt.EventId));
        Assert.Equal("STY02", await UsersPrimaryOrgAsync(cs, userId));
    }

    /// <summary>
    /// <b>F.</b> After a move dated 2 November, a move to another institution dated Monday 26 October
    /// 2026 (before the live row's start) is refused: 400, the same <c>fields</c> shape, code
    /// <c>BEFORE_CURRENT_ORGANISATION</c>, nearest valid day the live row's start; nothing written.
    /// <para><b>Red conditions.</b> Before C-13 the second request answers 200 (no before-live rule):
    /// the status assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task BeforeLiveRow_400_NamesItsStart()
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01");
        var client = GlobalAdminClient(_host);
        var first = await PutUserAsync(client, cs, userId,
            new { primaryOrgId = "STY02", effectiveFrom = "2026-11-02" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var before = await SnapshotAsync(cs, userId);

        var rsp = await PutUserAsync(client, cs, userId,
            new { primaryOrgId = "STY03", effectiveFrom = "2026-10-26" });

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        await AssertDateRefusalAsync(rsp, "BEFORE_CURRENT_ORGANISATION", "2026-11-02");
        Assert.Equal(before, await SnapshotAsync(cs, userId));
    }

    /// <summary>
    /// <b>F.</b> After a move dated 2 November, a second move on the SAME date changes the row in place:
    /// still two rows, the second now STY03 with its version +1; one more audit row (UPDATED); one more
    /// event (in place, no predecessor); and that event's projection row targets STY03.
    /// <para><b>Red conditions.</b> Before C-13 no organisation row or event is written, so the audit
    /// count (2) and the event count (2) assertions fail. The projection assertion is red if the row
    /// targets the old organisation or the actor's.</para>
    /// </summary>
    [Fact]
    public async Task Transfer_SecondTransferSameDate_UpdatesInPlace()
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01");
        var client = GlobalAdminClient(_host);
        var first = await PutUserAsync(client, cs, userId,
            new { primaryOrgId = "STY02", effectiveFrom = "2026-11-02" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var rsp = await PutUserAsync(client, cs, userId,
            new { primaryOrgId = "STY03", effectiveFrom = "2026-11-02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadOrgRowsAsync(cs, userId);
        Assert.Equal(2, rows.Count);
        Assert.Equal((new DateOnly(2026, 11, 2), (DateOnly?)null, "STY03", 3L),
            (rows[1].From, rows[1].To, rows[1].OrgId, rows[1].Version));

        var audit = await ReadOrgAuditAsync(cs, userId);
        Assert.Equal(2, audit.Count);
        Assert.Equal("UPDATED", audit[1].Action);
        Assert.Equal(((long?)2L, (long?)3L), (audit[1].VersionBefore, (long?)audit[1].VersionAfter));

        var events = await ReadEventsAsync(cs, userId, "UserOrganisationTransferred");
        Assert.Equal(2, events.Count);
        var second = events[1];
        Assert.True(second.Payload.GetProperty("inPlace").GetBoolean());
        Assert.True(!second.Payload.TryGetProperty("predecessorAssignmentId", out var pred)
                    || pred.ValueKind == JsonValueKind.Null);
        Assert.Equal("STY03", second.Payload.GetProperty("newOrgId").GetString());
        Assert.Equal("STY03", await ProjectionTargetOrgAsync(cs, second.EventId));
        Assert.Equal("STY03", await UsersPrimaryOrgAsync(cs, userId));
    }

    /// <summary>
    /// <b>F.</b> (NR-P7) A person created in STY01 today and moved to STY02 today ends with TWO rows:
    /// STY01 from the start of history to today, STY02 from today. The create's own first row, its
    /// CREATED audit row (by the acting admin) and its <c>UserOrganisationSeeded</c> event are asserted
    /// first, with the event's projection row targeting the request's organisation.
    /// <para><b>Red conditions.</b> Before C-13 the create writes no organisation row, audit row or
    /// event: the one-row assertion after the create fails, and (the move writing none either) so does
    /// the two-row assertion.</para>
    /// </summary>
    [Fact]
    public async Task Transfer_CreatedAndTransferredSameDay_TwoRows_CreatingOrganisationFrom00010101()
    {
        var cs = _harness.ConnectionString;
        var client = GlobalAdminClient(_host);
        var userId = await CreateUserAsync(client, "STY01");

        var created = await ReadOrgRowsAsync(cs, userId);
        var createdRow = Assert.Single(created);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)null, "STY01", 1L),
            (createdRow.From, createdRow.To, createdRow.OrgId, createdRow.Version));
        var createAudit = Assert.Single(await ReadOrgAuditAsync(cs, userId));
        Assert.Equal(("CREATED", "ADMIN_S148_ORG"), (createAudit.Action, createAudit.ActorId));
        var seeded = Assert.Single(await ReadEventsAsync(cs, userId, "UserOrganisationSeeded"));
        Assert.Equal("STY01", await ProjectionTargetOrgAsync(cs, seeded.EventId));

        var rsp = await PutUserAsync(client, cs, userId, new { primaryOrgId = "STY02" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var rows = await ReadOrgRowsAsync(cs, userId);
        Assert.Equal(2, rows.Count);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)Clock, "STY01"), (rows[0].From, rows[0].To, rows[0].OrgId));
        Assert.Equal((Clock, (DateOnly?)null, "STY02"), (rows[1].From, rows[1].To, rows[1].OrgId));
    }

    /// <summary>
    /// <b>R.</b> After STY01 → STY02 the live column <c>users.primary_org_id</c> is STY02 and a LocalHR
    /// scoped to STY01 only is refused (403) on the person; before the move the same LocalHR reads the
    /// person (200). <b>Discriminating assertion:</b> the 403. It passes today and must keep passing:
    /// access still follows the live column (ADR-038 D1), not the dated history. It would break if the
    /// move left the old organisation's HR able to read the person, or if access were switched to the
    /// dated read.
    /// </summary>
    [Fact]
    public async Task AfterTransfer_LiveColumnIsNew_OldOrganisationHrRefused()
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01");
        var hrSty01 = LocalHrClient(_host, "hr_s148_sty01_only", "STY01");

        var control = await hrSty01.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, control.StatusCode);

        var move = await PutUserAsync(GlobalAdminClient(_host), cs, userId, new { primaryOrgId = "STY02" });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        Assert.Equal("STY02", await UsersPrimaryOrgAsync(cs, userId));
        var rsp = await hrSty01.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // The refusals fire only when the organisation changes (the drawer's shape)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>R (i).</b> A drawer-shaped save carries the person's UNCHANGED organisation (STY01), an
    /// agreement-code change AC → HK and the date Monday 16 November 2026 (after the clock): 200, the
    /// agreement change is scheduled from 16 November, the organisation history keeps its one row from
    /// the start of history, and no <c>UserOrganisationTransferred</c> event is written.
    /// <b>Discriminating assertion:</b> the 200 (S141 scheduling must survive C-13). It passes today
    /// and breaks if the future-date refusal fires on a request that merely carries an organisation.
    /// </summary>
    [Fact]
    public async Task Transfer_DrawerFutureAgreementCodeDate_200_NoOrganisationRow()
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01", agreementCode: "AC");

        var rsp = await PutUserAsync(GlobalAdminClient(_host), cs, userId,
            new { primaryOrgId = "STY01", agreementCode = "HK", effectiveFrom = "2026-11-16" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var agreement = await ReadAgreementRowsAsync(cs, userId);
        Assert.Equal(2, agreement.Count);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)new DateOnly(2026, 11, 16), "AC"),
            (agreement[0].From, agreement[0].To, agreement[0].Code));
        Assert.Equal((new DateOnly(2026, 11, 16), (DateOnly?)null, "HK"),
            (agreement[1].From, agreement[1].To, agreement[1].Code));

        var rows = await ReadOrgRowsAsync(cs, userId);
        var only = Assert.Single(rows);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)null, "STY01"), (only.From, only.To, only.OrgId));
        Assert.Empty(await ReadEventsAsync(cs, userId, "UserOrganisationTransferred"));
    }

    /// <summary>
    /// <b>N (ii), M-11.</b> After a move STY01 → STY02 dated Monday 2 November 2026, a drawer-shaped
    /// save carrying the UNCHANGED organisation STY02, the correction AC → HK and the date Monday
    /// 26 October 2026 (before the live organisation row's start) is accepted: 200, the correction is
    /// written from 26 October, and no organisation row changes.
    /// <para><b>M-11 argument.</b> M-11 is "the 'not before the live row' refusal fires when the request
    /// carries an organisation, not when it changes one". Under it this request (it carries STY02) meets
    /// the before-live check with 26 October &lt; 2 November and answers the 400
    /// <c>BEFORE_CURRENT_ORGANISATION</c> instead of 200. The assertion it trips is
    /// <c>Assert.Equal(HttpStatusCode.OK, rsp.StatusCode)</c>, the first assertion after the request
    /// (NR-P29: the must-trip cell). Every other fixture's organisation row starts at the start of
    /// history, so no other request can be "before" it. The fact is green today (no before-live rule
    /// exists) and is red only under M-11, which is why it is N and not F.</para>
    /// </summary>
    [Fact]
    public async Task Transfer_DrawerCorrectionBeforeLaterTransfer_200()
    {
        var cs = _harness.ConnectionString;
        var userId = await SeedPersonAsync(cs, "STY01", agreementCode: "AC");
        var client = GlobalAdminClient(_host);
        var move = await PutUserAsync(client, cs, userId,
            new { primaryOrgId = "STY02", effectiveFrom = "2026-11-02" });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        var orgRowsBefore = await ReadOrgRowsAsync(cs, userId);

        var rsp = await PutUserAsync(client, cs, userId,
            new { primaryOrgId = "STY02", agreementCode = "HK", effectiveFrom = "2026-10-26" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var agreement = await ReadAgreementRowsAsync(cs, userId);
        Assert.Equal(2, agreement.Count);
        Assert.Equal((DateOnly.MinValue, (DateOnly?)new DateOnly(2026, 10, 26), "AC"),
            (agreement[0].From, agreement[0].To, agreement[0].Code));
        Assert.Equal((new DateOnly(2026, 10, 26), (DateOnly?)null, "HK"),
            (agreement[1].From, agreement[1].To, agreement[1].Code));
        Assert.Equal(orgRowsBefore, await ReadOrgRowsAsync(cs, userId));
        Assert.Single(await ReadEventsAsync(cs, userId, "UserOrganisationTransferred"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Shared helpers (internal static: reused by the other classes of this folder)
    // ═════════════════════════════════════════════════════════════════════

    internal sealed record OrgRow(Guid AssignmentId, DateOnly From, DateOnly? To, string OrgId, long Version);

    internal sealed record OrgAudit(
        Guid AssignmentId, string Action, long? VersionBefore, long VersionAfter, string ActorId);

    internal sealed record EventRow(Guid EventId, JsonElement Payload);

    internal sealed record AgreementRow(DateOnly From, DateOnly? To, string Code);

    internal static JwtTokenService TokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    /// <summary>A GlobalAdmin (employee id <c>ADMIN_S148_ORG</c>, no home organisation, GLOBAL scope).</summary>
    internal static HttpClient GlobalAdminClient(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var token = TokenService().GenerateToken(
            employeeId: "ADMIN_S148_ORG", name: "S148 Organisation Admin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A LocalHR whose scopes are ORG_ONLY over each of <paramref name="orgs"/>; home = the first.</summary>
    internal static HttpClient LocalHrClient(
        WebApplicationFactory<Program> host, string employeeId, params string[] orgs)
    {
        var client = host.CreateClient();
        var token = TokenService().GenerateToken(
            employeeId: employeeId, name: employeeId, role: StatsTidRoles.LocalHR,
            agreementCode: "HK", orgId: orgs[0],
            scopes: orgs.Select(o => new RoleScope(StatsTidRoles.LocalHR, o, "ORG_ONLY")).ToArray());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>A person made by <see cref="RegressionSeed"/> (users + profile + agreement + organisation row).</summary>
    internal static async Task<string> SeedPersonAsync(string cs, string orgId, string agreementCode = "AC")
    {
        var userId = "usr_s148_org_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await RegressionSeed.SeedEmployeeAsync(cs, userId, orgId, agreementCode: agreementCode);
        return userId;
    }

    /// <summary>A person made by the product's user create (<c>POST /api/admin/users</c>).</summary>
    internal static async Task<string> CreateUserAsync(HttpClient client, string orgId)
    {
        var userId = "usr_s148_new_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var rsp = await client.PostAsJsonAsync("/api/admin/users", new
        {
            userId,
            username = userId,
            password = "TestPassword123!",
            displayName = "S148 Organisation Test User",
            email = (string?)null,
            primaryOrgId = orgId,
            agreementCode = "AC",
            okVersion = "OK24",
        });
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);
        return userId;
    }

    /// <summary>PUT /api/admin/users/{id}, the If-Match read from the row itself (no GET, so a refused reader still works).</summary>
    internal static async Task<HttpResponseMessage> PutUserAsync(
        HttpClient client, string cs, string userId, object body)
    {
        var version = await ScalarLongAsync(cs, "SELECT version FROM users WHERE user_id = @u", ("u", userId));
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(req);
    }

    /// <summary>The one 400 shape both date refusals share (ruling 2.6).</summary>
    internal static async Task AssertDateRefusalAsync(HttpResponseMessage rsp, string code, string nearestValid)
    {
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Transfer date validation failed", body.GetProperty("error").GetString());
        var field = Assert.Single(body.GetProperty("fields").EnumerateArray().ToList());
        Assert.Equal("EffectiveFrom", field.GetProperty("field").GetString());
        Assert.Equal(code, field.GetProperty("code").GetString());
        var nearest = field.GetProperty("nearestValid").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { nearestValid }, nearest);
    }

    internal static async Task<IReadOnlyList<OrgRow>> ReadOrgRowsAsync(string cs, string userId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT assignment_id, effective_from, effective_to, org_id, version
            FROM user_organisations WHERE user_id = @u ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        var rows = new List<OrgRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new OrgRow(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                reader.GetString(3),
                reader.GetInt64(4)));
        }
        return rows;
    }

    internal static async Task<IReadOnlyList<OrgAudit>> ReadOrgAuditAsync(string cs, string userId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT assignment_id, action, version_before, version_after, actor_id
            FROM user_organisations_audit WHERE user_id = @u ORDER BY audit_id
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        var rows = new List<OrgAudit>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new OrgAudit(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.GetInt64(3),
                reader.GetString(4)));
        }
        return rows;
    }

    internal static async Task<IReadOnlyList<EventRow>> ReadEventsAsync(string cs, string userId, string eventType)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id, event_payload::text FROM outbox_events
            WHERE stream_id = @s AND event_type = @t ORDER BY outbox_id
            """, conn);
        cmd.Parameters.AddWithValue("s", $"user-{userId}");
        cmd.Parameters.AddWithValue("t", eventType);
        var rows = new List<EventRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new EventRow(reader.GetGuid(0), JsonSerializer.Deserialize<JsonElement>(reader.GetString(1))));
        return rows;
    }

    internal static async Task<IReadOnlyList<AgreementRow>> ReadAgreementRowsAsync(string cs, string userId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_from, effective_to, agreement_code
            FROM user_agreement_codes WHERE user_id = @u ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);
        var rows = new List<AgreementRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AgreementRow(
                reader.GetFieldValue<DateOnly>(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                reader.GetString(2)));
        }
        return rows;
    }

    internal static async Task<string?> ProjectionTargetOrgAsync(string cs, Guid eventId)
        => await ScalarStringAsync(cs, "SELECT target_org_id FROM audit_projection WHERE event_id = @e", ("e", eventId));

    internal static async Task<string?> ProjectionTargetResourceAsync(string cs, Guid eventId)
        => await ScalarStringAsync(cs, "SELECT target_resource_id FROM audit_projection WHERE event_id = @e", ("e", eventId));

    internal static async Task<string?> UsersPrimaryOrgAsync(string cs, string userId)
        => await ScalarStringAsync(cs, "SELECT primary_org_id FROM users WHERE user_id = @u", ("u", userId));

    /// <summary>Everything a refused transfer must leave alone, as one comparable string.</summary>
    internal static async Task<string> SnapshotAsync(string cs, string userId)
    {
        var rows = await ReadOrgRowsAsync(cs, userId);
        var audit = await ReadOrgAuditAsync(cs, userId);
        var events = await ScalarLongAsync(cs, "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", ("s", $"user-{userId}"));
        var primary = await UsersPrimaryOrgAsync(cs, userId);
        var version = await ScalarLongAsync(cs, "SELECT version FROM users WHERE user_id = @u", ("u", userId));
        return string.Join('|', string.Join(';', rows), audit.Count, events, primary, version);
    }

    internal static async Task<long> ScalarLongAsync(string cs, string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant at every call site; values go
        // through parameters, never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    internal static async Task<string?> ScalarStringAsync(string cs, string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant at every call site; values go
        // through parameters, never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteScalarAsync() as string;
    }
}
