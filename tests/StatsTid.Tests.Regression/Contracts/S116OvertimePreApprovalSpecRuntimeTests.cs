using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S116 / TASK-11603 — the per-route spec≡runtime gate for the OVERTIME PRE-APPROVAL family: the
/// four per-employee ops drained in Pass 3 (the 10-field bare-array list — approvedBy/approvedAt
/// null while PENDING and populated post-approve; the TRUE-201 7-field create; the 4-field approve
/// [+approvedBy] and 3-field reject SIBLINGS) plus the NEW S116 / TASK-11601 scope-bounded admin
/// list <c>GET /api/overtime/pre-approvals</c> (the 11-field element = the per-employee core +
/// non-null <c>employeeName</c>; typed from birth).
///
/// <para><b>This class AUTHORS TASK-11601's acceptance criteria</b> (11601's checklist consumes
/// these tests): (a) SCOPE-BOUNDEDNESS — an ORG_ONLY leader's list must NOT contain an
/// out-of-scope org's employee's pre-approval, while a GLOBAL actor sees both orgs' rows;
/// (b) INACTIVE-EXCLUSION — a deactivated employee's pre-approval (deactivated via the REAL admin
/// path, <c>PUT /api/admin/users/{id}</c> with <c>isActive: false</c>) must NOT appear in ANY
/// actor's list (the Step-0b convergent <c>is_active</c> pin); (c) an empty-scopes actor → 403;
/// (d) a multi-scope actor gets DEDUPED rows; (e) ALL THREE statuses enumerable
/// (PENDING/APPROVED/REJECTED — not PENDING-only); (f) <c>employeeName</c> non-null and equal to
/// the seeded <c>display_name</c>; (g) the spec≡runtime matcher assertion on the 11-field element.
/// (a) and (b) were demonstrated RED-FIRST against deliberately-inverted expectations — the RED
/// evidence is recorded in the TASK-11603 report.</para>
///
/// <para><b>Status truth:</b> every pre-approval status is driven through the REAL endpoints
/// (create → PENDING; PUT approve → APPROVED; PUT reject → REJECTED) — never SQL-faked. Seeds are
/// DISJOINT from the three named approval suites (STY02/STY05 + s78_*/s94_*/EMP_FR_AP_*). The
/// per-op tests live in their OWN Organisation (S116O03) so the admin-list org-scope assertions
/// (over S116O01/S116O02) can never observe them. Matcher + Support consumed AS-IS.</para>
///
/// <para><b>The RED-on-lie proof</b> (the S115 synthetic-contract pattern): the admin list's REAL
/// bare-array 200 response is matched against a deliberately-injected WRONG contract (the
/// delegation GET's OBJECT schema) — the matcher MUST throw on the array-ness lie.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S116OvertimePreApprovalSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string Mao = "S116OMAO";
    private const string InScopeOrg = "S116O01";   // the ORG_ONLY actor's org (e1 + e3)
    private const string OutScopeOrg = "S116O02";  // the out-of-scope org (e2)
    private const string OpsOrg = "S116O03";       // the per-employee op tests' own org

    private const string GlobalAdminId = "s116o_gadmin";
    private const string E1Name = "S116 Overtid Medarbejder En";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    // Fixture rows (driven through the REAL endpoints in InitializeAsync).
    private Guid _e1Pending;    // s116o_e1 (S116O01) — PENDING
    private Guid _e1Approved;   // s116o_e1 — APPROVED via PUT approve
    private Guid _e1Rejected;   // s116o_e1 — REJECTED via PUT reject
    private Guid _e2Pending;    // s116o_e2 (S116O02) — the out-of-scope row
    private Guid _e3Orphaned;   // s116o_e3 (S116O01) — its employee DEACTIVATED post-create

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline org tree)

        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await SeedAsync(conn);
        }

        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();

        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);

        // e1 (in-scope): three rows through the REAL state machine — one per status.
        _e1Pending = await CreateAsync(admin, "s116o_e1", "2026-01-01", "2026-01-31");
        _e1Approved = await CreateAsync(admin, "s116o_e1", "2026-02-01", "2026-02-28");
        await PutOkAsync(admin, $"/api/overtime/pre-approval/{_e1Approved}/approve", """{ "reason": "S116 godkendt" }""");
        _e1Rejected = await CreateAsync(admin, "s116o_e1", "2026-03-01", "2026-03-31");
        await PutOkAsync(admin, $"/api/overtime/pre-approval/{_e1Rejected}/reject", """{ "reason": "S116 afvist" }""");

        // e2 (out-of-scope org): the scope-boundedness counter-row.
        _e2Pending = await CreateAsync(admin, "s116o_e2", "2026-01-01", "2026-01-31");

        // e3 (in-scope): create FIRST, then DEACTIVATE the employee via the REAL admin path
        // (PUT /api/admin/users/{id} with isActive: false + admin-strict If-Match) — the
        // inactive-exclusion seed. No SQL flip: the users row goes inactive through the API.
        _e3Orphaned = await CreateAsync(admin, "s116o_e3", "2026-01-01", "2026-01-31");
        await PutOkAsync(admin, "/api/admin/users/s116o_e3", """{ "isActive": false }""", ifMatchVersion: 1);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The NEW admin list — TASK-11601's criteria (AUTHORED here, consumed there).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>(a) SCOPE-BOUNDEDNESS (demonstrated RED-first): the ORG_ONLY(S116O01) leader's
    /// list carries the in-scope employee's rows and must NOT contain the out-of-scope org's
    /// employee's pre-approval — while the GLOBAL actor sees BOTH orgs' rows.</summary>
    [Fact]
    public async Task AdminList_ScopeBounded_OrgOnlyExcludesOutOfScope_GlobalSeesBoth()
    {
        // The ORG_ONLY leader: exactly their org's rows.
        using var orgOnly = CreateActorClient("s116o_leader", StatsTidRoles.LocalLeader, InScopeOrg,
            new RoleScope(StatsTidRoles.LocalLeader, InScopeOrg, "ORG_ONLY"));
        var scopedRows = JsonDocument.Parse(await GetOkAsync(orgOnly, "/api/overtime/pre-approvals")).RootElement;

        Assert.Contains(_e1Pending, IdsOf(scopedRows));                       // the in-scope row IS there
        Assert.DoesNotContain(_e2Pending, IdsOf(scopedRows));                 // the out-of-scope row is NOT
        foreach (var row in scopedRows.EnumerateArray())                      // and NO row leaks the out-of-scope employee
            Assert.NotEqual("s116o_e2", row.GetProperty("employeeId").GetString());

        // The GLOBAL actor: both orgs' rows.
        using var global = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var globalRows = JsonDocument.Parse(await GetOkAsync(global, "/api/overtime/pre-approvals")).RootElement;
        Assert.Contains(_e1Pending, IdsOf(globalRows));
        Assert.Contains(_e2Pending, IdsOf(globalRows));
    }

    /// <summary>(b) INACTIVE-EXCLUSION (demonstrated RED-first; the Step-0b convergent pin): the
    /// deactivated employee's pre-approval must NOT appear in ANY actor's list — neither the
    /// GLOBAL actor's nor the ORG_ONLY actor's (the employee was in the ORG_ONLY actor's org).</summary>
    [Fact]
    public async Task AdminList_InactiveEmployeeExcluded_FromEveryActorsList()
    {
        using var global = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var globalRows = JsonDocument.Parse(await GetOkAsync(global, "/api/overtime/pre-approvals")).RootElement;
        Assert.DoesNotContain(_e3Orphaned, IdsOf(globalRows));

        using var orgOnly = CreateActorClient("s116o_leader", StatsTidRoles.LocalLeader, InScopeOrg,
            new RoleScope(StatsTidRoles.LocalLeader, InScopeOrg, "ORG_ONLY"));
        var scopedRows = JsonDocument.Parse(await GetOkAsync(orgOnly, "/api/overtime/pre-approvals")).RootElement;
        Assert.DoesNotContain(_e3Orphaned, IdsOf(scopedRows));
        foreach (var row in scopedRows.EnumerateArray())
            Assert.NotEqual("s116o_e3", row.GetProperty("employeeId").GetString());
    }

    /// <summary>(c) an actor with NO scopes → 403 (fail-closed, mirroring the pending loop).</summary>
    [Fact]
    public async Task AdminList_EmptyScopesActor_403()
    {
        using var noScopes = CreateActorClient("s116o_noscope", StatsTidRoles.LocalLeader, InScopeOrg /* no scopes */);
        using var response = await noScopes.SendAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/overtime/pre-approvals"));
        Assert.Equal(403, (int)response.StatusCode);
    }

    /// <summary>(d) a MULTI-scope actor (GLOBAL + ORG_ONLY over the same org) gets DEDUPED rows —
    /// the in-scope rows are admitted by BOTH scopes yet appear exactly once.</summary>
    [Fact]
    public async Task AdminList_MultiScopeActor_RowsDeduped()
    {
        using var multi = CreateActorClient("s116o_multi", StatsTidRoles.GlobalAdmin, Mao,
            new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL"),
            new RoleScope(StatsTidRoles.LocalLeader, InScopeOrg, "ORG_ONLY"));
        var rows = JsonDocument.Parse(await GetOkAsync(multi, "/api/overtime/pre-approvals")).RootElement;

        var ids = IdsOf(rows);
        Assert.Equal(ids.Count, ids.Distinct().Count()); // NO duplicates across the two scopes
        Assert.Contains(_e1Pending, ids);                // doubly-admitted (GLOBAL + ORG_ONLY), present ONCE
        Assert.Contains(_e2Pending, ids);                // GLOBAL-only admitted
    }

    /// <summary>(e)+(f)+(g): the spec≡runtime matcher on the 11-field element (incl. status enum
    /// fidelity), ALL THREE statuses present among the driven rows, and employeeName non-null and
    /// equal to the seeded display_name.</summary>
    [Fact]
    public async Task AdminList_SpecMatchesRuntime_AllThreeStatuses_EmployeeNameFromUsersJoin()
    {
        using var global = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, global,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/overtime/pre-approvals"),
            "/api/overtime/pre-approvals", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        // (e) all three statuses enumerable — NOT PENDING-only.
        Assert.Equal("PENDING", FindById(rows, _e1Pending).GetProperty("status").GetString());
        Assert.Equal("APPROVED", FindById(rows, _e1Approved).GetProperty("status").GetString());
        Assert.Equal("REJECTED", FindById(rows, _e1Rejected).GetProperty("status").GetString());

        // (f) employeeName rides the users admission join — non-null, the seeded display_name.
        foreach (var id in new[] { _e1Pending, _e1Approved, _e1Rejected })
            Assert.Equal(E1Name, FindById(rows, id).GetProperty("employeeName").GetString());

        // The APPROVED row's nullable pair populated; the PENDING row's null (the 11-field core).
        var approved = FindById(rows, _e1Approved);
        Assert.Equal(GlobalAdminId, approved.GetProperty("approvedBy").GetString());
        Assert.Equal(JsonValueKind.String, approved.GetProperty("approvedAt").ValueKind);
        var pending = FindById(rows, _e1Pending);
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("approvedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("approvedAt").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The four PER-EMPLOYEE ops (drained Pass 3) — own employees in S116O03.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The per-employee list (10-field element, NO employeeName): approvedBy/approvedAt
    /// NULL on the PENDING row, POPULATED after the REAL approve transition — both states matched
    /// against the spec.</summary>
    [Fact]
    public async Task PerEmployeeList_Get200_BareArraySchemaMatchesRuntime_ApprovedPairBothStates()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var id = await CreateAsync(admin, "s116o_e10", "2026-04-01", "2026-04-30");

        // PENDING state: the nullable pair is NULL.
        var pendingBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/overtime/s116o_e10/pre-approvals"),
            "/api/overtime/{employeeId}/pre-approvals", "get");
        var pendingRow = FindById(JsonDocument.Parse(pendingBody).RootElement, id);
        Assert.Equal("PENDING", pendingRow.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, pendingRow.GetProperty("approvedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, pendingRow.GetProperty("approvedAt").ValueKind);

        // Post-approve (the REAL transition): the pair is POPULATED.
        await PutOkAsync(admin, $"/api/overtime/pre-approval/{id}/approve", """{ "reason": "S116 liste godkendt" }""");
        var approvedBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/overtime/s116o_e10/pre-approvals"),
            "/api/overtime/{employeeId}/pre-approvals", "get");
        var approvedRow = FindById(JsonDocument.Parse(approvedBody).RootElement, id);
        Assert.Equal("APPROVED", approvedRow.GetProperty("status").GetString());
        Assert.Equal(GlobalAdminId, approvedRow.GetProperty("approvedBy").GetString());
        Assert.Equal(JsonValueKind.String, approvedRow.GetProperty("approvedAt").ValueKind);
    }

    /// <summary>Create — a TRUE 201 (asserted EXACTLY) with the 7-field creation body.</summary>
    [Fact]
    public async Task Create_Post201_ExactStatusAndSevenFieldBodyMatchRuntime()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/overtime/pre-approval",
            """{ "employeeId": "s116o_e11", "periodStart": "2026-05-01", "periodEnd": "2026-05-31", "maxHours": 12.5, "reason": "S116 opret-op" }"""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/overtime/pre-approval", "post");
        Assert.Equal(201, truth.StatusCode);         // the committed contract declares exactly 201
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "POST /api/overtime/pre-approval (201)");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("s116o_e11", root.GetProperty("employeeId").GetString());
        Assert.Equal("PENDING", root.GetProperty("status").GetString());
        Assert.Equal("S116 opret-op", root.GetProperty("reason").GetString());
    }

    /// <summary>Approve — the 4-field sibling (+approvedBy, populated with the actor).</summary>
    [Fact]
    public async Task Approve_Put200_FourFieldSiblingSchemaMatchesRuntime_ApprovedByPopulated()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var id = await CreateAsync(admin, "s116o_e12", "2026-06-01", "2026-06-30");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/overtime/pre-approval/{id}/approve",
                """{ "reason": "S116 godkend-op" }"""),
            "/api/overtime/pre-approval/{id}/approve", "put");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("APPROVED", root.GetProperty("status").GetString());
        Assert.Equal(GlobalAdminId, root.GetProperty("approvedBy").GetString());
        Assert.Equal("S116 godkend-op", root.GetProperty("reason").GetString());
    }

    /// <summary>Reject — the 3-field sibling (NO approvedBy on the wire).</summary>
    [Fact]
    public async Task Reject_Put200_ThreeFieldSiblingSchemaMatchesRuntime()
    {
        using var admin = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var id = await CreateAsync(admin, "s116o_e12", "2026-07-01", "2026-07-31");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/overtime/pre-approval/{id}/reject",
                """{ "reason": "S116 afvis-op" }"""),
            "/api/overtime/pre-approval/{id}/reject", "put");

        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal("REJECTED", root.GetProperty("status").GetString());
        Assert.Equal("S116 afvis-op", root.GetProperty("reason").GetString());
        Assert.False(root.TryGetProperty("approvedBy", out _), "reject is the 3-field sibling — approvedBy must NOT be on its wire");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The RED-on-lie proof (the S115 synthetic-contract pattern).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The admin list's REAL bare-array 200 response is matched against a
    /// deliberately-injected WRONG contract (the delegation GET's OBJECT schema): the matcher MUST
    /// throw on the array-ness lie — the exact lie class a mis-declared <c>.Produces&lt;T&gt;</c>
    /// tells — while the TRUTH contract passes on the same response.</summary>
    [Fact]
    public async Task Gate_IsRed_OnInjectedWrongShapeContract()
    {
        using var global = SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminId, Mao);
        var body = await GetOkAsync(global, "/api/overtime/pre-approvals");

        // The truth (the committed bare-array contract) passes on the real response…
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/overtime/pre-approvals", "get");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "truth");

        // …the lie (another op's OBJECT schema injected behind the same 200) is RED on it.
        var wrongSchema = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/reporting-lines/delegate", "get").Schema;
        var lie = new SpecRuntimeMatcher.SuccessContract(200, wrongSchema);
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertSuccessMatches(_spec, lie, 200, body, "injected-shape-lie"));
        Assert.Contains("OBJECT", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Support ──

    /// <summary>Create a pre-approval through the REAL endpoint (201) and return its id.</summary>
    private static async Task<Guid> CreateAsync(HttpClient client, string employeeId, string periodStart, string periodEnd)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/overtime/pre-approval",
            $$"""{ "employeeId": "{{employeeId}}", "periodStart": "{{periodStart}}", "periodEnd": "{{periodEnd}}", "maxHours": 10, "reason": "S116 seed" }"""));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Pre-approval create for {employeeId} returned {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task PutOkAsync(HttpClient client, string url, string jsonBody, long? ifMatchVersion = null)
    {
        using var response = await client.SendAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, url, jsonBody, ifMatchVersion));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 200)
            throw new XunitException($"Setup call PUT {url} returned {(int)response.StatusCode}: {body}");
    }

    private static async Task<string> GetOkAsync(HttpClient client, string url)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, url));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 200)
            throw new XunitException($"GET {url} returned {(int)response.StatusCode}: {body}");
        return body;
    }

    /// <summary>A client for an arbitrary actor/role/scope set (the ORG_ONLY leader, the
    /// empty-scopes actor, the multi-scope actor). Mirrors the Support helper's JWT minting;
    /// Support itself consumed AS-IS (S115 compatibility contract).</summary>
    private HttpClient CreateActorClient(string actorId, string role, string orgId, params RoleScope[] scopes)
    {
        var client = _factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "HK", orgId: orgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static List<Guid> IdsOf(JsonElement array)
    {
        var ids = new List<Guid>();
        foreach (var el in array.EnumerateArray())
            ids.Add(el.GetProperty("id").GetGuid());
        return ids;
    }

    private static JsonElement FindById(JsonElement array, Guid id)
    {
        foreach (var el in array.EnumerateArray())
            if (el.GetProperty("id").GetGuid() == id)
                return el;
        throw new XunitException($"Expected a row with id {id} in: {array.GetRawText()}");
    }

    // ── Fixture seed — FRESH orgs/users, disjoint from the three named approval suites. NO
    //    pre-approval rows are seeded here — every row is driven through the REAL endpoints. ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S116OMAO', 'S116 Overtid Ministerie',  'MAO',          NULL,       '/S116OMAO/',          'AC', 'OK24'),
                ('S116O01',  'S116 Overtid Indenfor',    'ORGANISATION', 'S116OMAO', '/S116OMAO/S116O01/',  'HK', 'OK24'),
                ('S116O02',  'S116 Overtid Udenfor',     'ORGANISATION', 'S116OMAO', '/S116OMAO/S116O02/',  'HK', 'OK24'),
                ('S116O03',  'S116 Overtid Op-styrelse', 'ORGANISATION', 'S116OMAO', '/S116OMAO/S116O03/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                ('s116o_e1',  's116o_e1',  '$2a$11$fake', 'S116 Overtid Medarbejder En', 's116o_e1@test.dk',  'S116O01', 'HK', 'OK24', TRUE),
                ('s116o_e2',  's116o_e2',  '$2a$11$fake', 'S116 Overtid Medarbejder To', 's116o_e2@test.dk',  'S116O02', 'HK', 'OK24', TRUE),
                ('s116o_e3',  's116o_e3',  '$2a$11$fake', 'S116 Fratrådt Medarbejder',   's116o_e3@test.dk',  'S116O01', 'HK', 'OK24', TRUE),
                ('s116o_e10', 's116o_e10', '$2a$11$fake', 'S116 Liste-op Medarbejder',   's116o_e10@test.dk', 'S116O03', 'HK', 'OK24', TRUE),
                ('s116o_e11', 's116o_e11', '$2a$11$fake', 'S116 Opret-op Medarbejder',   's116o_e11@test.dk', 'S116O03', 'HK', 'OK24', TRUE),
                ('s116o_e12', 's116o_e12', '$2a$11$fake', 'S116 Afgør-op Medarbejder',   's116o_e12@test.dk', 'S116O03', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
