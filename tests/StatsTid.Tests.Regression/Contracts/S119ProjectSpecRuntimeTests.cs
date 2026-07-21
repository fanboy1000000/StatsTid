using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S119 / TASK-11902 — the per-route spec≡runtime gate for the PROJECTS family drained in
/// retrofit Pass 6 (TASK-11900): list GET + create POST 201 (the SAME 4-member
/// <c>ProjectResponse</c> at BOTH sites — the S112 sibling-record rule, pinned live),
/// available GET (the 5-member <c>+selected</c> shape and its flip after select), select POST
/// (200, the 2-member echo, NO request body — the declared-bodyless member) and deselect
/// DELETE (204 + EMPTY body), project PUT (the 2-member <c>{projectId, updated}</c>) and
/// project DELETE (204 + empty).
///
/// <para><b>The PRECONDITION-FREE family pin (Step-0b Reviewer N1 — pinned, not observed):</b>
/// NO If-Match/ETag exists anywhere in this family by design. Every mutation here is sent
/// WITHOUT any precondition header and must SUCCEED, and every response is asserted to carry
/// NO ETag header (<see cref="S119ContractAssert.AssertNoEtag"/>) — if the family ever grows a
/// concurrency surface, these facts go RED.</para>
///
/// <para><b>Per-op policy pins (the S119 P7 map, incl. the two BY-DESIGN EmployeeOrAbove
/// WRITES):</b> select POST and deselect DELETE are self-service employee writes — driven by a
/// REAL seeded employee user (the selection container FK-requires a users row) and proven to
/// SUCCEED at the Employee floor; create/update/delete are <c>LocalAdminOrAbove</c> — the same
/// employee actor is pinned 403 on all three.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test (the established
/// Contracts-suite harness — never the compose stack on :5432). The init.sql STY02 project
/// seed rows (DRIFT-01/PROJ-ALPHA/PROJ-BETA/SYSDEV-01/VEDL-01) are READ-ASSERTED ONLY — no
/// test mutates them; ALL mutations run under the S119-fresh org <c>S119PRJ1</c> with project
/// codes <c>S119_*</c>, actor <c>s119j_gadmin</c> (JWT-only) and employee <c>s119j_emp</c>
/// (seeded via the canonical <see cref="RegressionSeed"/> — the users-row FK target for the
/// selection container). Matcher + Support + S118ContractAssert consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S119ProjectSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string AdminActorId = "s119j_gadmin";
    private const string EmployeeId = "s119j_emp";
    private const string Org = "S119PRJ1";
    private const string SeedOrg = "STY02"; // init.sql seed org — READ-ASSERTED ONLY

    /// <summary>The EXACT 4 camelCase members of <c>ProjectResponse</c> — the ONE record at
    /// BOTH sites (list rows AND the 201 create; the sibling rule).</summary>
    private static readonly string[] ProjectKeys = { "projectId", "projectCode", "projectName", "sortOrder" };

    /// <summary>The EXACT 5 members of <c>AvailableProjectResponse</c> (<c>+selected</c>).</summary>
    private static readonly string[] AvailableKeys = { "projectId", "projectCode", "projectName", "sortOrder", "selected" };

    /// <summary>The EXACT 2 members of <c>ProjectSelectionResponse</c>.</summary>
    private static readonly string[] SelectionKeys = { "projectId", "selected" };

    /// <summary>The EXACT 2 members of <c>ProjectUpdateResponse</c>.</summary>
    private static readonly string[] UpdateKeys = { "projectId", "updated" };

    /// <summary>The 5 init.sql STY02 seed project codes (init.sql:1133–1139) — read-asserted.</summary>
    private static readonly string[] Sty02SeedCodes =
        { "DRIFT-01", "PROJ-ALPHA", "PROJ-BETA", "SYSDEV-01", "VEDL-01" };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
        // The S119 org + the REAL employee user (users-row FK target for the selection
        // container writes) via the canonical seed helper — ensureOrg creates S119PRJ1.
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, EmployeeId, Org, "AC", "OK24");
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — GET /api/projects/{orgId} (bare array) — the STY02 seed rows, READ-ONLY.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The list GET against the init.sql STY02 seed world (READ-ASSERTED ONLY — this
    /// class never mutates STY02): the matcher walks every row; exactly the 5 seed rows serve,
    /// each the exact 4-member shape, sort_order 1..5 preserved.</summary>
    [Fact]
    public async Task List_Get200_Sty02SeedRows_ReadAssertedOnly()
    {
        using var admin = AdminClient();
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/projects/{SeedOrg}"),
            "/api/projects/{orgId}", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        Assert.Equal(5, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
            S118ContractAssert.AssertExactKeySet(row, ProjectKeys, "project list row (STY02 seed)");
        var codes = rows.EnumerateArray().Select(r => r.GetProperty("projectCode").GetString()).ToList();
        foreach (var seedCode in Sty02SeedCodes)
            Assert.Contains(seedCode, codes);
        Assert.Equal(1, FindByCode(rows, "DRIFT-01").GetProperty("sortOrder").GetInt32());
        Assert.Equal(5, FindByCode(rows, "VEDL-01").GetProperty("sortOrder").GetInt32());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — POST /api/projects/{orgId} (201) — the SIBLING pin at BOTH sites.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The exact-201 create + matcher + the 4-member key set, then the LIST re-read
    /// serving the SAME row with the IDENTICAL member set — the one-<c>ProjectResponse</c>
    /// sibling rule proven at both sites on live bytes. PRECONDITION-FREE pinned: no If-Match
    /// sent, and NO ETag on either response.</summary>
    [Fact]
    public async Task Create_Post201Exact_SameSiblingShapeAtBothSites_PreconditionFree()
    {
        using var admin = AdminClient();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/projects/{Org}",
            """{ "projectCode": "S119_CRT", "projectName": "S119 Kontraktprojekt", "sortOrder": 7 }"""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED
        S119ContractAssert.AssertNoEtag(response, "project create 201");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/projects/{orgId}", "post");
        Assert.Equal(201, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "POST /api/projects/{orgId} (201)");

        var created = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(created, ProjectKeys, "project create 201 (sibling site 1)");
        Assert.Equal("S119_CRT", created.GetProperty("projectCode").GetString());
        Assert.Equal("S119 Kontraktprojekt", created.GetProperty("projectName").GetString());
        Assert.Equal(7, created.GetProperty("sortOrder").GetInt32());
        var projectId = created.GetProperty("projectId").GetGuid();

        // Sibling site 2 — the list row is the SAME record shape with the SAME values.
        var listBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/projects/{Org}"),
            "/api/projects/{orgId}", "get");
        var listRow = FindByCode(JsonDocument.Parse(listBody).RootElement, "S119_CRT");
        S118ContractAssert.AssertExactKeySet(listRow, ProjectKeys, "project list row (sibling site 2)");
        Assert.Equal(projectId, listRow.GetProperty("projectId").GetGuid());
        Assert.Equal(7, listRow.GetProperty("sortOrder").GetInt32());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — GET /api/projects/{orgId}/available — the `selected` flip.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The 5-member available shape BOTH sides of the flip: before select
    /// <c>selected: false</c>, after the REAL select POST <c>selected: true</c> — the matcher
    /// walks both reads, proving the boolean is live per-employee state, not a constant.</summary>
    [Fact]
    public async Task Available_Get200_FiveMemberRows_SelectedFlipsAfterSelect()
    {
        using var admin = AdminClient();
        using var employee = EmployeeClient();
        var projectId = await CreateProjectAsync(admin, "S119_AVL", "S119 Tilvalgsprojekt", 1);

        var before = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/projects/{Org}/available"),
            "/api/projects/{orgId}/available", "get");
        var beforeRow = FindByCode(JsonDocument.Parse(before).RootElement, "S119_AVL");
        S118ContractAssert.AssertExactKeySet(beforeRow, AvailableKeys, "available row (pre-select)");
        Assert.False(beforeRow.GetProperty("selected").GetBoolean());

        await SelectAsync(employee, projectId); // the REAL select POST

        var after = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/projects/{Org}/available"),
            "/api/projects/{orgId}/available", "get");
        var afterRow = FindByCode(JsonDocument.Parse(after).RootElement, "S119_AVL");
        S118ContractAssert.AssertExactKeySet(afterRow, AvailableKeys, "available row (post-select)");
        Assert.True(afterRow.GetProperty("selected").GetBoolean()); // THE flip
        Assert.Equal(projectId, afterRow.GetProperty("projectId").GetGuid());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 4 — POST .../select/{projectId} — declared-BODYLESS; EmployeeOrAbove WRITE.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The declared-bodyless member (openapi-bodyless-declared.txt, 8→9): the POST is
    /// sent with NO request body and succeeds — 200 through the matcher with the EXACT
    /// 2-member <c>{projectId, selected: true}</c> echo. Driven by the EMPLOYEE client: the
    /// self-service write floor (EmployeeOrAbove BY DESIGN) pinned POSITIVELY.</summary>
    [Fact]
    public async Task Select_Post200_TwoMemberEcho_NoRequestBody_EmployeeWriteSucceeds()
    {
        using var admin = AdminClient();
        using var employee = EmployeeClient();
        var projectId = await CreateProjectAsync(admin, "S119_SEL", "S119 Valgprojekt", 1);

        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/projects/{Org}/select/{projectId}")); // NO body — declared bodyless
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode);
        S119ContractAssert.AssertNoEtag(response, "select POST 200");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(
            _spec, "/api/projects/{orgId}/select/{projectId}", "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "POST .../select/{projectId} (200)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, SelectionKeys, "select POST 200");
        Assert.Equal(projectId, root.GetProperty("projectId").GetGuid());
        Assert.True(root.GetProperty("selected").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 5 — DELETE .../select/{projectId} — 204 + EMPTY body; EmployeeOrAbove WRITE.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Select (real POST) → deselect: the declared 204 = status + EMPTY body, both
    /// matcher-asserted, at the Employee floor (the second by-design self-service write). The
    /// available re-read proves the deselect landed (<c>selected</c> back to false).</summary>
    [Fact]
    public async Task Deselect_Delete204_EmptyBody_EmployeeWriteSucceeds()
    {
        using var admin = AdminClient();
        using var employee = EmployeeClient();
        var projectId = await CreateProjectAsync(admin, "S119_DSL", "S119 Fravalgsprojekt", 1);
        await SelectAsync(employee, projectId);

        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, $"/api/projects/{Org}/select/{projectId}"),
            "/api/projects/{orgId}/select/{projectId}", "delete");

        var after = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/projects/{Org}/available"),
            "/api/projects/{orgId}/available", "get");
        var row = FindByCode(JsonDocument.Parse(after).RootElement, "S119_DSL");
        Assert.False(row.GetProperty("selected").GetBoolean()); // the deselect landed
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 6 — PUT /api/projects/{orgId}/{projectId} — 2-member; PRECONDITION-FREE.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The update PUT with the POST-DROP request key set (<c>projectName</c> +
    /// <c>sortOrder</c> only — the never-bound <c>projectCode</c> key is gone, the S112
    /// accepted-delta class): 200 through the matcher, the EXACT 2-member
    /// <c>{projectId, updated: true}</c>, sent WITHOUT any precondition header (pinned — the
    /// family has no If-Match surface) and serving NO ETag. The list re-read proves the edit
    /// landed (name + sortOrder changed, projectCode untouched).</summary>
    [Fact]
    public async Task Update_Put200_TwoMemberEcho_PreconditionFreePinned()
    {
        using var admin = AdminClient();
        var projectId = await CreateProjectAsync(admin, "S119_UPD", "S119 Før-navn", 1);

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{Org}/{projectId}",
            """{ "projectName": "S119 Efter-navn", "sortOrder": 9 }""")); // NO precondition header — pinned
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode); // succeeds WITHOUT If-Match — the family pin
        S119ContractAssert.AssertNoEtag(response, "project PUT 200");

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/projects/{orgId}/{projectId}", "put");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "PUT /api/projects/{orgId}/{projectId} (200)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, UpdateKeys, "project PUT 200");
        Assert.Equal(projectId, root.GetProperty("projectId").GetGuid());
        Assert.True(root.GetProperty("updated").GetBoolean());

        var listBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/projects/{Org}"),
            "/api/projects/{orgId}", "get");
        var row = FindByCode(JsonDocument.Parse(listBody).RootElement, "S119_UPD"); // code untouched by the edit
        Assert.Equal("S119 Efter-navn", row.GetProperty("projectName").GetString());
        Assert.Equal(9, row.GetProperty("sortOrder").GetInt32());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 7 — DELETE /api/projects/{orgId}/{projectId} — 204 + empty; PRECONDITION-FREE.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The soft deactivate: declared 204 = status + EMPTY body through the matcher,
    /// sent WITHOUT any precondition header (pinned). The list re-read proves the soft delete
    /// (the row leaves the active read — <c>is_active</c> filtered).</summary>
    [Fact]
    public async Task Delete_204_EmptyBody_PreconditionFreePinned_SoftDeactivate()
    {
        using var admin = AdminClient();
        var projectId = await CreateProjectAsync(admin, "S119_DEL", "S119 Sletteprojekt", 1);

        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete, $"/api/projects/{Org}/{projectId}"),
            "/api/projects/{orgId}/{projectId}", "delete"); // no precondition header — the family pin

        var listBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/projects/{Org}"),
            "/api/projects/{orgId}", "get");
        Assert.DoesNotContain(JsonDocument.Parse(listBody).RootElement.EnumerateArray(),
            r => r.GetProperty("projectId").GetGuid() == projectId); // soft-deactivated out of the active read
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The P7 admin-floor pins — the SAME employee actor, 403 on all three admin writes.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The per-op policy map's other half: create/update/delete are
    /// <c>LocalAdminOrAbove</c> — the SAME employee identity that SUCCEEDS on select/deselect
    /// is 403 on all three admin mutations (per-op pins, not a generalization).</summary>
    [Fact]
    public async Task AdminMutations_403_ForTheEmployeeActor_PolicyFloorPins()
    {
        using var admin = AdminClient();
        using var employee = EmployeeClient();
        var projectId = await CreateProjectAsync(admin, "S119_403", "S119 Adgangsprojekt", 1);

        using (var create = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/projects/{Org}",
            """{ "projectCode": "S119_403B", "projectName": "S119 Nægtet", "sortOrder": 2 }""")))
            Assert.Equal(403, (int)create.StatusCode);

        using (var update = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/projects/{Org}/{projectId}",
            """{ "projectName": "S119 Nægtet", "sortOrder": 2 }""")))
            Assert.Equal(403, (int)update.StatusCode);

        using (var delete = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Delete, $"/api/projects/{Org}/{projectId}")))
            Assert.Equal(403, (int)delete.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The RED-on-lie proof (the S119 pass's injected-lie demonstration).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The established injected-lie technique (S117/S118): the REAL create 201 body
    /// passes against the COMMITTED truth contract (GREEN), then the SAME body is matched
    /// against an IN-MEMORY corrupted copy of the spec whose <c>ProjectResponse</c> schema
    /// gains a phantom <c>required</c> member — the matcher MUST go RED through the
    /// required-fidelity path with the phantom member NAMED. The committed spec on disk is
    /// never touched (revert-free by construction).</summary>
    [Fact]
    public async Task Gate_IsRed_OnInjectedPhantomRequiredMember_AndGreenOnTheCommittedTruth()
    {
        using var admin = AdminClient();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/projects/{Org}",
            """{ "projectCode": "S119_RED", "projectName": "S119 Løgnedetektor", "sortOrder": 1 }"""));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(201, (int)response.StatusCode);

        const string path = "/api/projects/{orgId}";

        // GREEN — the committed truth passes on the real 201.
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, path, "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "truth");

        // RED — the same response against the spec with a phantom required member injected.
        var lieNode = JsonNode.Parse(_spec.GetRawText())!;
        var schema = lieNode["components"]!["schemas"]!["StatsTid.Backend.Api.Contracts.ProjectResponse"]!;
        ((JsonArray)schema["required"]!).Add("s119PhantomMember");
        var lieSpec = JsonDocument.Parse(lieNode.ToJsonString()).RootElement.Clone();

        var lieContract = SpecRuntimeMatcher.ResolveSuccessContract(lieSpec, path, "post");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertSuccessMatches(lieSpec, lieContract, 201, body, "injected-required-lie"));

        Assert.Contains("s119PhantomMember", ex.Message, StringComparison.Ordinal);
        Assert.Contains("REQUIRED", ex.Message, StringComparison.Ordinal); // the required-fidelity path, not a kind check
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient AdminClient()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, AdminActorId, Org);

    /// <summary>The REAL seeded employee's client (ORG_ONLY Employee scope over the S119 org) —
    /// the self-service select/deselect actor AND the 403 actor for the admin writes. Mirrors
    /// the Support helper's JWT minting (Support consumed AS-IS).</summary>
    private HttpClient EmployeeClient()
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
            employeeId: EmployeeId, name: EmployeeId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Create a project through the REAL admin POST (precondition-free by family
    /// design); returns the projectId. Throws with the response body on any non-201.</summary>
    private async Task<Guid> CreateProjectAsync(HttpClient client, string code, string name, int sortOrder)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/projects/{Org}",
            $$"""{ "projectCode": "{{code}}", "projectName": "{{name}}", "sortOrder": {{sortOrder}} }"""));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Project create for {code} returned {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("projectId").GetGuid();
    }

    /// <summary>Select through the REAL bodyless POST; throws with the body on any non-200.</summary>
    private static async Task SelectAsync(HttpClient employee, Guid projectId)
    {
        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/projects/{Org}/select/{projectId}"));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 200)
            throw new XunitException($"Project select for {projectId} returned {(int)response.StatusCode}: {body}");
    }

    private static JsonElement FindByCode(JsonElement array, string projectCode)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("projectCode").GetString(), projectCode, StringComparison.Ordinal))
                return el;
        throw new XunitException($"Expected a project row for '{projectCode}' in: {array.GetRawText()}");
    }
}
