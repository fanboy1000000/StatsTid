using System.Globalization;
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
/// S119 / TASK-11902 — the per-route spec≡runtime gate for the LOCAL-AGREEMENT-PROFILE chain
/// drained in retrofit Pass 6 (TASK-11900): the flexible-precondition PUT (the program's FIRST
/// live <c>If-None-Match: *</c> surface — ADR-018 D7), the by-key GET (ETag fidelity
/// <c>"&lt;version&gt;"</c>), and the history GET (immutable rows — NO ETag), all on the ONE
/// shared 14-member <c>LocalAgreementProfileResponse</c> serving the THREE success sites.
///
/// <para><b>The create path IS the proof:</b> <c>local_agreement_profiles</c> has NO seed —
/// every profile in this class is born through the REAL <c>If-None-Match: *</c> PUT
/// (200 + ETag <c>"1"</c>), which simultaneously proves the first-create branch and
/// establishes the state every other fact reads.</para>
///
/// <para><b>ORDERING INDEPENDENCE (the Step-0b Reviewer W2 hard criterion):</b> xUnit
/// guarantees no <c>[Fact]</c> order, so EVERY fact self-creates its profile under its OWN
/// S119-prefixed org key (<c>S119PRF_*</c> — one org per fact, listed in
/// <see cref="SeedOrgsAsync"/>); no fact assumes a pre-existing profile. (Each fact also gets
/// a FRESH testcontainer via IAsyncLifetime, making independence structural twice over.)</para>
///
/// <para><b>The error-surface exclusion (the S118 rule, held):</b> the PUT's non-2xx bodies
/// are deliberately UNTYPED in the spec (the 412 <c>currentState</c> envelope included) — the
/// 428 and 412 facts pin STATUS + structural body presence only, never a declared schema.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test (the established
/// Contracts-suite harness — never the compose stack on :5432). Orgs <c>S119PRF_*</c> are
/// S119-fresh SQL INPUT rows; the profile key namespace is (<c>S119AGR</c>, <c>OKS119</c>) —
/// DISJOINT from the boot seeders (AC/HK/PROSA × OK24/OK26) and from the 9 repository-level
/// Config profile suites (which drive the repo directly under their own orgs). Actors
/// <c>s119p_gadmin</c>/<c>s119p_emp</c> are JWT-only. All profile writes go through the REAL
/// PUT. Matcher + Support + S118ContractAssert consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S119ProfileSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string AdminActorId = "s119p_gadmin";
    private const string EmployeeActorId = "s119p_emp";
    private const string AgreementCode = "S119AGR";
    private const string OkVersion = "OKS119";

    /// <summary>The EXACT 14 camelCase members of <c>LocalAgreementProfileResponse</c> — the
    /// ONE shared record at all three success sites (PUT 200 / GET 200 / history rows).</summary>
    private static readonly string[] ProfileKeys =
    {
        "profileId", "orgId", "agreementCode", "okVersion",
        "effectiveFrom", "effectiveTo",
        "weeklyNormHours", "maxFlexBalance", "flexCarryoverMax",
        "maxOvertimeHoursPerPeriod", "overtimeRequiresPreApproval",
        "createdBy", "createdAt", "version",
    };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
        await SeedOrgsAsync();
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — PUT .../profile/... with If-None-Match: * (the FIRST-CREATE branch).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The no-seed create path: <c>If-None-Match: *</c> PUT → 200 (the declared
    /// single success status — never a 201 on this op), matcher against the committed
    /// <c>LocalAgreementProfileResponse</c>, the EXACT 14-member key set, ETag <c>"1"</c>
    /// (the repo-assigned first version), <c>effectiveTo</c> PRESENT-and-null (the open
    /// profile), nulls preserved on the inherit-central members, and the actor echo.</summary>
    [Fact]
    public async Task CreatePut200_IfNoneMatchStar_FirstCreate_EtagOne()
    {
        const string org = "S119PRF_CRT";
        using var admin = AdminClient(org);
        using var response = await admin.SendAsync(S119ContractAssert.WithIfNoneMatchStar(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, ProfilePath(org),
                ProfileBodyJson(Today(), maxFlexBalance: 30.0m))));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode); // the declared success — the create IS a 200 here

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, ProfileSpecPath, "put");
        Assert.Equal(200, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "profile PUT 200 (first create)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, ProfileKeys, "profile PUT 200 (first create)");
        Assert.Equal(org, root.GetProperty("orgId").GetString());
        Assert.Equal(AgreementCode, root.GetProperty("agreementCode").GetString());
        Assert.Equal(OkVersion, root.GetProperty("okVersion").GetString());
        Assert.Equal(1L, root.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("effectiveTo").ValueKind);      // open profile
        Assert.Equal(30.0m, root.GetProperty("maxFlexBalance").GetDecimal());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("weeklyNormHours").ValueKind);  // inherit-central null, PRESENT
        Assert.Equal(AdminActorId, root.GetProperty("createdBy").GetString());
        Assert.Equal(1L, S118ContractAssert.EtagVersion(response)); // ETag "1" — the version stamp
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — GET .../profile/... (ETag fidelity "<version>", not a constant).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Self-create (v1) → in-place If-Match edit (v2) → GET: matcher + the 14-member
    /// key set, and ETag FIDELITY — the header is <c>"2"</c> equal to the body's
    /// <c>version</c>, proving <c>"&lt;version&gt;"</c> tracks the row rather than echoing a
    /// constant <c>"1"</c>. The MODIFIED branch's created_by/created_at preservation rides the
    /// same read (same profileId, same creator).</summary>
    [Fact]
    public async Task Get200_AfterInPlaceEdit_EtagTracksTheBodyVersion()
    {
        const string org = "S119PRF_GET";
        using var admin = AdminClient(org);
        var effectiveFrom = Today();
        var created = await CreateProfileAsync(admin, org, effectiveFrom, maxFlexBalance: 30.0m);
        await PutOkAsync(admin, org, ProfileBodyJson(effectiveFrom, maxFlexBalance: 40.0m), ifMatchVersion: 1);

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Get, ProfilePath(org)));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)response.StatusCode);

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, ProfileSpecPath, "get");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "profile GET 200");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, ProfileKeys, "profile GET 200");
        Assert.Equal(2L, root.GetProperty("version").GetInt64());
        Assert.Equal(2L, S118ContractAssert.EtagVersion(response)); // ETag == body version, live at 2
        Assert.Equal(created.ProfileId, root.GetProperty("profileId").GetGuid()); // in-place: SAME row
        Assert.Equal(40.0m, root.GetProperty("maxFlexBalance").GetDecimal());
        Assert.Equal(AdminActorId, root.GetProperty("createdBy").GetString()); // preserved across the edit
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1b — PUT with If-Match (the UPDATE-IN-PLACE branch; version bump).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Self-create (v1) → same-day If-Match <c>"1"</c> PUT: 200 through the matcher,
    /// the 14-member key set, version bumped to 2 with ETag <c>"2"</c>, the SAME profileId
    /// (MODIFIED in place, not a supersession), and the edited value round-tripped.</summary>
    [Fact]
    public async Task IfMatchPut200_InPlaceEdit_VersionBumpsOnTheSameRow()
    {
        const string org = "S119PRF_PUT";
        using var admin = AdminClient(org);
        var effectiveFrom = Today();
        var created = await CreateProfileAsync(admin, org, effectiveFrom, maxFlexBalance: 30.0m);

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, ProfilePath(org),
            ProfileBodyJson(effectiveFrom, maxFlexBalance: 40.0m), ifMatchVersion: 1));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)response.StatusCode);

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, ProfileSpecPath, "put");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "profile PUT 200 (If-Match edit)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, ProfileKeys, "profile PUT 200 (If-Match edit)");
        Assert.Equal(created.ProfileId, root.GetProperty("profileId").GetGuid()); // in-place, same row
        Assert.Equal(2L, root.GetProperty("version").GetInt64());
        Assert.Equal(40.0m, root.GetProperty("maxFlexBalance").GetDecimal());
        Assert.Equal(2L, S118ContractAssert.EtagVersion(response));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — GET .../history (bare array of the SAME record; immutable ⇒ NO ETag).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Self-create BACKDATED (today−14) → supersede via If-Match <c>"1"</c> at a later
    /// effectiveFrom (today) ⇒ close-then-insert. The history GET serves the CLOSED predecessor
    /// through the matcher (bare array of the shared 14-member record), with <c>effectiveTo</c>
    /// now POPULATED (the other nullable state, live) — and NO ETag header (history rows are
    /// immutable; the header's absence is the pin).</summary>
    [Fact]
    public async Task History_Get200_ClosedPredecessorRow_ListShape_NoEtagHeader()
    {
        const string org = "S119PRF_HIS";
        using var admin = AdminClient(org);
        var backdated = Today().AddDays(-14);
        var created = await CreateProfileAsync(admin, org, backdated, maxFlexBalance: 30.0m);
        await PutOkAsync(admin, org, ProfileBodyJson(Today(), maxFlexBalance: 40.0m), ifMatchVersion: 1); // supersede

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Get, ProfilePath(org) + "/history"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)response.StatusCode);

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, ProfileSpecPath + "/history", "get");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "profile history GET 200");

        Assert.Null(response.Headers.ETag); // immutable history — NO ETag, pinned

        var rows = JsonDocument.Parse(body).RootElement;
        Assert.Equal(1, rows.GetArrayLength());
        var closed = rows[0];
        S118ContractAssert.AssertExactKeySet(closed, ProfileKeys, "history row (closed predecessor)");
        Assert.Equal(created.ProfileId, closed.GetProperty("profileId").GetGuid());
        Assert.Equal(JsonValueKind.String, closed.GetProperty("effectiveTo").ValueKind); // POPULATED — closed
        Assert.Equal(30.0m, closed.GetProperty("maxFlexBalance").GetDecimal());          // the pre-supersession value
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The composed error preconditions (statuses pinned; bodies deliberately untyped).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A precondition-less PUT is 428 (ADR-018 D7 — exactly one of If-Match /
    /// If-None-Match must be supplied). The error body is deliberately UNTYPED in the spec
    /// (the S118 exclusion, held): only its structural presence is asserted — an
    /// <c>error</c> member exists; no declared schema is consulted.</summary>
    [Fact]
    public async Task Put428_WhenNoPreconditionHeaderSupplied()
    {
        const string org = "S119PRF_PRE";
        using var admin = AdminClient(org);
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, ProfilePath(org), ProfileBodyJson(Today(), maxFlexBalance: 30.0m)));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(428, (int)response.StatusCode);
        var root = JsonDocument.Parse(body).RootElement;
        Assert.True(root.TryGetProperty("error", out _)); // structural presence only — body stays undeclared
    }

    /// <summary>The stale-precondition surface, composed AS THE FE DOES: (a) If-Match
    /// <c>"1"</c> after the row advanced to v2 ⇒ 412 with the version mismatch surfaced and a
    /// <c>currentState</c> narrowed STRUCTURALLY (version 2 visible — the FE's retry read),
    /// never against a declared schema (the exclusion boundary); (b) <c>If-None-Match: *</c>
    /// when a profile already exists ⇒ 412 (the create-collision half of the flexible
    /// precondition).</summary>
    [Fact]
    public async Task Put412_OnStaleIfMatch_AndOnIfNoneMatchStarCollision()
    {
        const string org = "S119PRF_STA";
        using var admin = AdminClient(org);
        var effectiveFrom = Today();
        await CreateProfileAsync(admin, org, effectiveFrom, maxFlexBalance: 30.0m);
        await PutOkAsync(admin, org, ProfileBodyJson(effectiveFrom, maxFlexBalance: 40.0m), ifMatchVersion: 1); // → v2

        // (a) stale If-Match "1" against v2.
        using (var stale = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, ProfilePath(org),
            ProfileBodyJson(effectiveFrom, maxFlexBalance: 50.0m), ifMatchVersion: 1)))
        {
            var body = await stale.Content.ReadAsStringAsync();
            Assert.Equal(412, (int)stale.StatusCode);
            var root = JsonDocument.Parse(body).RootElement;
            Assert.Equal(1L, root.GetProperty("expectedVersion").GetInt64());
            Assert.Equal(2L, root.GetProperty("actualVersion").GetInt64());
            // currentState: structural narrowing ONLY (no declared schema exists for this body).
            var current = root.GetProperty("currentState");
            Assert.Equal(JsonValueKind.Object, current.ValueKind);
            Assert.Equal(2L, current.GetProperty("version").GetInt64());
        }

        // (b) If-None-Match: * while a profile exists — the first-create precondition collides.
        using var collision = await admin.SendAsync(S119ContractAssert.WithIfNoneMatchStar(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, ProfilePath(org),
                ProfileBodyJson(effectiveFrom, maxFlexBalance: 50.0m))));
        Assert.Equal(412, (int)collision.StatusCode);
    }

    /// <summary>The P7 per-op policy pin: the profile PUT is <c>LocalAdminOrAbove</c> — an
    /// EMPLOYEE actor (even with an ORG_ONLY scope covering the target org and a well-formed
    /// first-create precondition) is 403.</summary>
    [Fact]
    public async Task Put403_ForTheEmployeeActor_PolicyFloorPin()
    {
        const string org = "S119PRF_EMP";
        using var employee = EmployeeClient(org);
        using var response = await employee.SendAsync(S119ContractAssert.WithIfNoneMatchStar(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, ProfilePath(org),
                ProfileBodyJson(Today(), maxFlexBalance: 30.0m))));
        Assert.Equal(403, (int)response.StatusCode);
    }

    // ─────────────────────────────── clients ───────────────────────────────

    private HttpClient AdminClient(string orgId)
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, AdminActorId, orgId);

    private HttpClient EmployeeClient(string orgId)
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
            employeeId: EmployeeActorId, name: EmployeeActorId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ─────────────────────────────── paths / bodies / drives ───────────────────────────────

    private const string ProfileSpecPath = "/api/config/{orgId}/profile/{agreementCode}/{okVersion}";

    private static string ProfilePath(string orgId)
        => $"/api/config/{orgId}/profile/{AgreementCode}/{OkVersion}";

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.Date);

    /// <summary>The PUT body: <c>effectiveFrom</c> (required) + <c>maxFlexBalance</c> only —
    /// the one overridable field with NO alignment policy (WeeklyNormHours is Monday-locked;
    /// keeping it null keeps every fact date-independent). Omitted members serve as
    /// inherit-central nulls on the response.</summary>
    private static string ProfileBodyJson(DateOnly effectiveFrom, decimal maxFlexBalance)
        => $$"""
           { "effectiveFrom": "{{effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}}",
             "maxFlexBalance": {{maxFlexBalance.ToString(CultureInfo.InvariantCulture)}} }
           """;

    /// <summary>Self-create the fact's OWN profile through the REAL <c>If-None-Match: *</c>
    /// PUT (no seed exists — the ordering-independence primitive); returns (profileId,
    /// ETag-version). Throws with the response body on any non-200.</summary>
    private async Task<(Guid ProfileId, long EtagVersion)> CreateProfileAsync(
        HttpClient client, string orgId, DateOnly effectiveFrom, decimal maxFlexBalance)
    {
        using var response = await client.SendAsync(S119ContractAssert.WithIfNoneMatchStar(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, ProfilePath(orgId),
                ProfileBodyJson(effectiveFrom, maxFlexBalance))));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 200)
            throw new XunitException($"Profile first-create PUT for {orgId} returned {(int)response.StatusCode}: {body}");
        var root = JsonDocument.Parse(body).RootElement;
        return (root.GetProperty("profileId").GetGuid(), S118ContractAssert.EtagVersion(response));
    }

    private async Task PutOkAsync(HttpClient client, string orgId, string jsonBody, long ifMatchVersion)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, ProfilePath(orgId), jsonBody, ifMatchVersion: ifMatchVersion));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 200)
            throw new XunitException($"Profile If-Match PUT for {orgId} returned {(int)response.StatusCode}: {body}");
    }

    /// <summary>One S119 org INPUT row per fact (the ordering-independence org-key partition;
    /// the S117/S118 SQL org-seed precedent — orgs are input data, profiles are ONLY ever
    /// created through the real PUT).</summary>
    private async Task SeedOrgsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                       materialized_path, agreement_code, ok_version) VALUES
                ('S119PRF_CRT', 'S119 Profil Org (create)',   'ORGANISATION', NULL, '/S119PRF_CRT/', 'AC', 'OK24'),
                ('S119PRF_GET', 'S119 Profil Org (get)',      'ORGANISATION', NULL, '/S119PRF_GET/', 'AC', 'OK24'),
                ('S119PRF_PUT', 'S119 Profil Org (edit)',     'ORGANISATION', NULL, '/S119PRF_PUT/', 'AC', 'OK24'),
                ('S119PRF_HIS', 'S119 Profil Org (history)',  'ORGANISATION', NULL, '/S119PRF_HIS/', 'AC', 'OK24'),
                ('S119PRF_PRE', 'S119 Profil Org (428)',      'ORGANISATION', NULL, '/S119PRF_PRE/', 'AC', 'OK24'),
                ('S119PRF_STA', 'S119 Profil Org (412)',      'ORGANISATION', NULL, '/S119PRF_STA/', 'AC', 'OK24'),
                ('S119PRF_EMP', 'S119 Profil Org (403)',      'ORGANISATION', NULL, '/S119PRF_EMP/', 'AC', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
