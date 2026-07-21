using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S118 / TASK-11802 — the per-route spec≡runtime gate for the AGREEMENT-CONFIGS family drained
/// in retrofit Pass 5 (TASK-11800): the 8 parent ops (list + status-filter branch, by-id GET
/// with embedded entitlements, by-code list, create 201, clone 201, PUT 200, publish/archive
/// envelopes) + the 4 child entitlement ops (list / POST 201 / PUT 200 / DELETE 204).
///
/// <para><b>RULING #1 PIN (the program's first wire-change ruling, the dead-branch class):</b>
/// the create AND clone 201s are ALWAYS the full 48-member <c>AgreementConfigResponse</c> —
/// the backend now INSERT…RETURNINGs inside the tx, so the legacy <c>{configId}</c>-only
/// fallback branch is structurally dead. These tests are the CONTRACT pin: the 201 bodies are
/// asserted against the EXACT 48-member key set (missing AND extra keys both named-fail), so a
/// resurrected fallback fork is RED forever.</para>
///
/// <para><b>RULING #2 PIN (the drift-repair class):</b> the by-id GET's embedded
/// <c>entitlements[]</c> rows now carry <c>fullDayOnly</c> — the pre-S118 inline mapper was the
/// DRIFTED 15-member copy. The by-id test asserts the EXACT 16-member child key set on a live
/// embedded row (a CARE_DAY child created through the REAL child POST, whose
/// <c>fullDayOnly: true</c> is guard-forced — non-vacuous).</para>
///
/// <para><b>Wire-byte discipline:</b> the 3 wire-changed sites in this family (create 201,
/// clone 201 — ruling #1; the by-id GET's additive child member — ruling #2) are DELTA-pinned
/// (the new truth asserted directly); every other op is a byte-faithful shape the matcher
/// asserts against the committed spec. Enum fidelity rides the matcher on every walk —
/// <c>status</c> {DRAFT, ACTIVE, ARCHIVED} and <c>normModel</c> {WEEKLY_HOURS,
/// ANNUAL_ACTIVITY} are exercised on LIVE values (DRAFT create / ACTIVE publish;
/// WEEKLY_HOURS create / ANNUAL_ACTIVITY clone+PUT).</para>
///
/// <para><b>DECLARED CROSS-DOMAIN PRODUCT DEFECT (found by this gate, declared not fixed —
/// src/** is out of scope):</b> the archive endpoint's EVERY success path and the publish
/// endpoint's SUPERSESSION branch 500 (PostgresException 22P02 — the handlers pass BARE
/// strings "DRAFT"/"ACTIVE"/"ARCHIVED" as audit previous/new data into <c>::jsonb</c> casts).
/// See <see cref="PublishSupersedeAndArchive_DeclaredProductDefect_SuccessPathsBlockedByBareStringAuditJson"/>
/// — the defect TRIPWIRE that goes RED when the backend fixes it, forcing the flip to the
/// real envelope assertions.</para>
///
/// <para><b>Seed discipline:</b> a FRESH testcontainer per test (the established harness);
/// every agreement code is <c>S118AGC_*</c>/<c>S118AGE_*</c> with okVersion <c>OKS118</c> —
/// DISJOINT from the boot seeders (AC/HK/PROSA × OK24/OK26), from
/// <c>AgreementConfigConcurrencyTests</c> (<c>CON_AGR_*</c>) and
/// <c>AgreementConfigAtomicTests</c> (<c>FR_AGR_*</c>), and from every EntitlementConfig suite
/// (<c>OK_S30POST_*</c>/<c>OK_S68RESET_*</c>/<c>OK_CASEA_*</c>/<c>OK_CASEC_*</c>/
/// <c>OK_S73FDO_*</c> + their CARE_DAY/SENIOR_DAY/CHILD_SICK/SPECIAL_HOLIDAY rows under
/// AC/HK/PROSA×OK24/OK26). Every mutation drives its OWN row(s) created through the REAL
/// endpoints — no SQL-faked state. Matcher + Support consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S118AgreementConfigSpecRuntimeTests : IAsyncLifetime
{
    private const string ActorId = "s118c_gadmin";
    private const string JwtOrg = "S118CM"; // JWT claim only — config-family audit rows are GLOBAL (no org FK)
    private const string OkVersion = "OKS118";

    /// <summary>The ruling #1 anchor: the EXACT 48 camelCase members of
    /// <c>AgreementConfigResponse</c> — the create/clone/PUT wire truth.</summary>
    private static readonly string[] FullEntityKeys =
    {
        "configId", "agreementCode", "okVersion", "status", "version",
        "weeklyNormHours", "normPeriodWeeks", "normModel", "annualNormHours",
        "maxFlexBalance", "flexCarryoverMax",
        "hasOvertime", "hasMerarbejde", "overtimeThreshold50", "overtimeThreshold100",
        "eveningSupplementEnabled", "nightSupplementEnabled", "weekendSupplementEnabled", "holidaySupplementEnabled",
        "eveningStart", "eveningEnd", "nightStart", "nightEnd",
        "eveningRate", "nightRate", "weekendSaturdayRate", "weekendSundayRate", "holidayRate",
        "onCallDutyEnabled", "onCallDutyRate",
        "callInWorkEnabled", "callInMinimumHours", "callInRate",
        "travelTimeEnabled", "workingTravelRate", "nonWorkingTravelRate",
        "maxDailyHours", "minimumRestHours", "restPeriodDerogationAllowed",
        "weeklyMaxHoursReferencePeriod", "voluntaryUnsocialHoursAllowed",
        "createdBy", "createdAt", "updatedAt", "publishedAt", "archivedAt", "clonedFromId", "description",
    };

    /// <summary>The ruling #2 anchor: the 16-member shared <c>EntitlementConfigResponse</c>
    /// child shape (incl. <c>fullDayOnly</c> — the member the drifted inline mapper omitted).</summary>
    private static readonly string[] ChildEntitlementKeys =
    {
        "configId", "entitlementType", "agreementCode", "okVersion",
        "annualQuota", "accrualModel", "resetMonth", "carryoverMax",
        "proRateByPartTime", "isPerEpisode", "minAge", "description",
        "fullDayOnly", "effectiveFrom", "effectiveTo", "version",
    };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (AC/HK/PROSA baseline configs)
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 1 — GET /api/agreement-configs (bare array; BOTH query branches).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The unfiltered AND the ?status=DRAFT branch serialize the SAME shared record
    /// (one wire contract, two repo reads). The matcher walks EVERY element — the boot-seeded
    /// ACTIVE rows exercise status enum fidelity alongside the fresh DRAFT row.</summary>
    [Fact]
    public async Task List_Get200_BareArray_BothQueryBranches()
    {
        using var admin = Admin();
        await CreateConfigAsync(admin, "S118AGC_LST");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/agreement-configs"),
            "/api/agreement-configs", "get");
        var row = FindByCode(JsonDocument.Parse(body).RootElement, "S118AGC_LST");
        Assert.Equal("DRAFT", row.GetProperty("status").GetString());          // in the declared enum set, live
        Assert.Equal("WEEKLY_HOURS", row.GetProperty("normModel").GetString());
        Assert.Equal(1L, row.GetProperty("version").GetInt64());               // in-body If-Match source

        var filtered = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/agreement-configs?status=DRAFT"),
            "/api/agreement-configs", "get");
        _ = FindByCode(JsonDocument.Parse(filtered).RootElement, "S118AGC_LST"); // the filter branch serves the same shape
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 4 — POST /api/agreement-configs (201) — RULING #1 PIN (create).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>RULING #1: the create 201 is ALWAYS the full 48-member entity — never the dead
    /// <c>{configId}</c> fallback. Exact-201 status + matcher + the EXACT 48-member key set
    /// (both directions). ETag "1" = the RETURNING-hydrated first-create version.</summary>
    [Fact]
    public async Task Create_Post201Exact_RulingOnePin_Always48MemberFullEntity()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/agreement-configs", ConfigRequestJson("S118AGC_CRT")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/agreement-configs", "post");
        Assert.Equal(201, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "POST /api/agreement-configs (201)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, FullEntityKeys, "create 201 (ruling #1)");
        Assert.Equal("S118AGC_CRT", root.GetProperty("agreementCode").GetString());
        Assert.Equal(OkVersion, root.GetProperty("okVersion").GetString());
        Assert.Equal("DRAFT", root.GetProperty("status").GetString());
        Assert.Equal(1L, root.GetProperty("version").GetInt64());
        Assert.Equal("WEEKLY_HOURS", root.GetProperty("normModel").GetString());
        Assert.Equal(37.0m, root.GetProperty("weeklyNormHours").GetDecimal()); // decimal fidelity off RETURNING
        Assert.Equal(ActorId, root.GetProperty("createdBy").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("clonedFromId").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("publishedAt").ValueKind);
        Assert.Equal(1L, S118ContractAssert.EtagVersion(response));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 5 — POST /api/agreement-configs/{configId}/clone (201) — RULING #1 PIN (clone).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>RULING #1 applied to the clone fork: same full-48-member pin, plus
    /// <c>clonedFromId</c> = the source id (the lineage member the dead fallback could never
    /// carry) and the copied ANNUAL_ACTIVITY normModel exercising the second enum value.</summary>
    [Fact]
    public async Task Clone_Post201Exact_RulingOnePin_Always48MemberFullEntity()
    {
        using var admin = Admin();
        var (sourceId, _, _) = await CreateConfigAsync(admin, "S118AGC_CLN", normModel: "ANNUAL_ACTIVITY");

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post,
            $"/api/agreement-configs/{sourceId}/clone?agreementCode=S118AGC_CLN2&okVersion={OkVersion}"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)response.StatusCode); // the EXACT status — a 200 here is RED

        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/agreement-configs/{configId}/clone", "post");
        Assert.Equal(201, truth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "POST /api/agreement-configs/{configId}/clone (201)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, FullEntityKeys, "clone 201 (ruling #1)");
        Assert.Equal("S118AGC_CLN2", root.GetProperty("agreementCode").GetString());
        Assert.Equal("DRAFT", root.GetProperty("status").GetString());
        Assert.Equal(1L, root.GetProperty("version").GetInt64());
        Assert.Equal("ANNUAL_ACTIVITY", root.GetProperty("normModel").GetString()); // copied, in-set, live
        Assert.Equal(sourceId, root.GetProperty("clonedFromId").GetGuid());          // the lineage pin
        Assert.NotEqual(sourceId, root.GetProperty("configId").GetGuid());
        Assert.Equal(1L, S118ContractAssert.EtagVersion(response));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — GET /api/agreement-configs/{agreementCode}/{okVersion} (bare array).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ByCode_Get200_BareArraySchemaMatchesRuntime()
    {
        using var admin = Admin();
        var (configId, _, _) = await CreateConfigAsync(admin, "S118AGC_BYC");

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/agreement-configs/S118AGC_BYC/{OkVersion}"),
            "/api/agreement-configs/{agreementCode}/{okVersion}", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(configId, rows[0].GetProperty("configId").GetGuid());
        Assert.Equal("S118AGC_BYC", rows[0].GetProperty("agreementCode").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 6 — PUT /api/agreement-configs/{configId} (200; admin-strict If-Match).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>If-Match composed AS THE FE DOES: the create 201's ETag is the version the PUT
    /// sends. The 200 shares the same 48-member record (the sibling-record rule) — key-set
    /// asserted here too; normModel flips to ANNUAL_ACTIVITY (the second enum value, live).</summary>
    [Fact]
    public async Task Update_Put200_IfMatchComposedFromTheCreateEtag()
    {
        using var admin = Admin();
        var (configId, etagVersion, _) = await CreateConfigAsync(admin, "S118AGC_PUT");
        Assert.Equal(1L, etagVersion);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put, $"/api/agreement-configs/{configId}",
                ConfigRequestJson("S118AGC_PUT", normModel: "ANNUAL_ACTIVITY"), ifMatchVersion: etagVersion),
            "/api/agreement-configs/{configId}", "put");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, FullEntityKeys, "PUT 200");
        Assert.Equal(configId, root.GetProperty("configId").GetGuid());
        Assert.Equal(2L, root.GetProperty("version").GetInt64());                    // bumped under If-Match
        Assert.Equal("ANNUAL_ACTIVITY", root.GetProperty("normModel").GetString());
        Assert.Equal("DRAFT", root.GetProperty("status").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — GET /api/agreement-configs/{configId} — RULING #2 PIN (fullDayOnly on
    //  the embedded child rows).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>RULING #2: the by-id GET's embedded <c>entitlements[]</c> rows carry the FULL
    /// 16-member shared child shape incl. <c>fullDayOnly</c> (the member the drifted inline
    /// mapper omitted at this — the third — former mapper site). The child is a CARE_DAY row
    /// created through the REAL child POST, whose <c>fullDayOnly: true</c> is guard-forced, so
    /// the pin is non-vacuous. Root = the 48 members + <c>entitlements</c> +
    /// <c>entitlementsReadOnly</c>.</summary>
    [Fact]
    public async Task ById_Get200_RulingTwoPin_EmbeddedEntitlementRowsCarryFullDayOnly()
    {
        using var admin = Admin();
        var (parentId, _, _) = await CreateConfigAsync(admin, "S118AGE_BID");
        await CreateChildAsync(admin, parentId);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/agreement-configs/{parentId}"),
            "/api/agreement-configs/{configId}", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(
            root, FullEntityKeys.Concat(new[] { "entitlements", "entitlementsReadOnly" }).ToArray(), "by-id 200");
        Assert.False(root.GetProperty("entitlementsReadOnly").GetBoolean()); // single config for the key

        var rows = root.GetProperty("entitlements");
        Assert.Equal(1, rows.GetArrayLength());
        var child = rows[0];
        S118ContractAssert.AssertExactKeySet(child, ChildEntitlementKeys, "embedded entitlements[] row (ruling #2)");
        Assert.Equal("CARE_DAY", child.GetProperty("entitlementType").GetString());
        Assert.True(child.GetProperty("fullDayOnly").GetBoolean());          // THE repaired member, live TRUE
        Assert.Equal(1L, child.GetProperty("version").GetInt64());           // the child If-Match source
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 7 — POST /api/agreement-configs/{configId}/publish (200 envelope; BOTH
    //  archivedConfigId branches).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The bespoke publish envelope on its REACHABLE branch — ALL 4 keys ALWAYS
    /// emitted (nullable-always-present, never optional-key): a first publish has NO prior
    /// ACTIVE ⇒ <c>archivedConfigId</c> SERVED null (key present); <c>status</c> "ACTIVE"
    /// exercises the enum set live.
    ///
    /// <para><b>DECLARED CROSS-DOMAIN DEFECT (out of tests/** scope):</b> the SUPERSESSION
    /// branch (<c>archivedConfigId</c> populated) is UNREACHABLE — see
    /// <see cref="PublishSupersedeAndArchive_DeclaredProductDefect_SuccessPathsBlockedByBareStringAuditJson"/>.</para></summary>
    [Fact]
    public async Task Publish_Post200_Envelope_NoPriorActiveBranch_ArchivedConfigIdServedNull()
    {
        using var admin = Admin();
        string[] envelopeKeys = { "configId", "status", "archivedConfigId", "publishedAt" };

        // No prior ACTIVE for the key: archivedConfigId is SERVED null (key present).
        var (firstId, firstVersion, _) = await CreateConfigAsync(admin, "S118AGC_PUB");
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post,
                $"/api/agreement-configs/{firstId}/publish", jsonBody: null, ifMatchVersion: firstVersion),
            "/api/agreement-configs/{configId}/publish", "post");
        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, envelopeKeys, "publish 200 (no-prior-ACTIVE branch)");
        Assert.Equal(firstId, root.GetProperty("configId").GetGuid());
        Assert.Equal("ACTIVE", root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("archivedConfigId").ValueKind); // nullable-ALWAYS-present
        Assert.Equal(JsonValueKind.String, root.GetProperty("publishedAt").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 8 — POST .../archive + op 7's supersession branch: DECLARED PRODUCT DEFECT.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>DECLARED CROSS-DOMAIN PRODUCT DEFECT (found by this per-route gate — the first
    /// HTTP-level drive of these success paths; src/** is out of this task's scope, so the
    /// defect is DECLARED, not fixed):</b> the archive endpoint's audit call passes the BARE
    /// strings <c>existing.Status.ToString()</c> / <c>"ARCHIVED"</c> as
    /// <c>previous_data</c>/<c>new_data</c>, and the publish endpoint's ADR-019 D1 dual-emit
    /// passes bare <c>"ACTIVE"</c>/<c>"ARCHIVED"</c> — the repo casts both parameters
    /// <c>::jsonb</c>, so EVERY archive and EVERY supersession publish rolls back with
    /// PostgresException 22P02 and surfaces 500 where the committed spec declares 200. The
    /// existing REPO-level suites knew the strings are invalid JSON (their comments say so)
    /// and passed hand-built JSON — masking the ENDPOINT bug from every prior run.
    ///
    /// <para><b>This test is the defect TRIPWIRE, not an acceptance:</b> it pins today's
    /// failure signature so the moment the backend fixes the audit JSON this test goes RED and
    /// the fix task MUST replace it with the real envelope assertions (archive:
    /// <c>{configId, status: "ARCHIVED", archivedAt}</c>; publish-supersede:
    /// <c>archivedConfigId</c> = the superseded config id).</para>
    /// </summary>
    [Fact]
    public async Task PublishSupersedeAndArchive_DeclaredProductDefect_SuccessPathsBlockedByBareStringAuditJson()
    {
        using var admin = Admin();

        // Archive — blocked on EVERY invocation (bare-string previous/new data).
        var (archiveId, archiveVersion, _) = await CreateConfigAsync(admin, "S118AGC_ARC");
        using (var archive = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{archiveId}/archive",
            jsonBody: null, ifMatchVersion: archiveVersion)))
        {
            var body = await archive.Content.ReadAsStringAsync();
            Assert.Equal(500, (int)archive.StatusCode); // the spec declares 200 — the gate's verdict is honest: DEFECT
            Assert.Contains("22P02", body, StringComparison.Ordinal);
            // The tx rolled back: the row is still DRAFT at version 1 (no partial archive).
            var after = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
                _spec, admin,
                SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/agreement-configs/{archiveId}"),
                "/api/agreement-configs/{configId}", "get");
            var afterRoot = JsonDocument.Parse(after).RootElement;
            Assert.Equal("DRAFT", afterRoot.GetProperty("status").GetString());
            Assert.Equal(1L, afterRoot.GetProperty("version").GetInt64());
        }

        // Publish over a prior ACTIVE — blocked on the ADR-019 D1 dual-emit (bare strings).
        var (activeId, activeVersion, _) = await CreateConfigAsync(admin, "S118AGC_SUP");
        using (var firstPublish = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{activeId}/publish",
            jsonBody: null, ifMatchVersion: activeVersion)))
        {
            Assert.Equal(200, (int)firstPublish.StatusCode); // the reachable branch works
        }

        var (draftId, draftVersion, _) = await CreateConfigAsync(admin, "S118AGC_SUP");
        using var supersede = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{draftId}/publish",
            jsonBody: null, ifMatchVersion: draftVersion));
        var supersedeBody = await supersede.Content.ReadAsStringAsync();
        Assert.Equal(500, (int)supersede.StatusCode); // spec declares 200 — DEFECT, declared upward
        Assert.Contains("22P02", supersedeBody, StringComparison.Ordinal);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 9 — GET /api/agreement-configs/{configId}/entitlements (bare array).
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChildEntitlements_List_Get200_BareArraySchemaMatchesRuntime()
    {
        using var admin = Admin();
        var (parentId, _, _) = await CreateConfigAsync(admin, "S118AGE_LST");
        var (childId, _, _) = await CreateChildAsync(admin, parentId);

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/agreement-configs/{parentId}/entitlements"),
            "/api/agreement-configs/{configId}/entitlements", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal(childId, rows[0].GetProperty("configId").GetGuid());
        Assert.Equal("CARE_DAY", rows[0].GetProperty("entitlementType").GetString());
        Assert.True(rows[0].GetProperty("fullDayOnly").GetBoolean());
        Assert.Equal("S118AGE_LST", rows[0].GetProperty("agreementCode").GetString()); // parent-derived key
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Ops 10+11 — POST (201) then PUT (200) on the child sub-resource: one flow,
    //  status-per-verb on the ONE shared child record.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>POST creates (TRUE 201, asserted exactly; the 16-member shared child record)
    /// and the same-day PUT edits IN PLACE (Case C — same configId, version 2) under the
    /// admin-strict If-Match composed from the POST's ETag. The CARE_DAY full-day-only ruling
    /// round-trips through both verbs (<c>fullDayOnly: true</c> guard-forced).</summary>
    [Fact]
    public async Task ChildEntitlements_Post201Exact_ThenPut200_OnTheSharedChildRecord()
    {
        using var admin = Admin();
        var (parentId, _, _) = await CreateConfigAsync(admin, "S118AGE_WRT");

        // POST — the exact-201 assertion + the matcher on the shared child record.
        using var created = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{parentId}/entitlements", ChildCreateJson));
        var createdBody = await created.Content.ReadAsStringAsync();

        Assert.Equal(201, (int)created.StatusCode); // the EXACT status — a 200 here is RED

        var postTruth = SpecRuntimeMatcher.ResolveSuccessContract(
            _spec, "/api/agreement-configs/{configId}/entitlements", "post");
        Assert.Equal(201, postTruth.StatusCode);
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, postTruth, 201, createdBody,
            "POST /api/agreement-configs/{configId}/entitlements (201)");

        var createdRoot = JsonDocument.Parse(createdBody).RootElement;
        S118ContractAssert.AssertExactKeySet(createdRoot, ChildEntitlementKeys, "child POST 201");
        var childId = createdRoot.GetProperty("configId").GetGuid();
        Assert.Equal(2.0m, createdRoot.GetProperty("annualQuota").GetDecimal());
        Assert.True(createdRoot.GetProperty("fullDayOnly").GetBoolean());
        var etagVersion = S118ContractAssert.EtagVersion(created);
        Assert.Equal(1L, etagVersion);

        // PUT — same-day Case C in-place edit under If-Match from the POST's ETag.
        var putBody = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Put,
                $"/api/agreement-configs/{parentId}/entitlements/{childId}",
                ChildPutJson(), ifMatchVersion: etagVersion),
            "/api/agreement-configs/{configId}/entitlements/{entitlementConfigId}", "put");

        var putRoot = JsonDocument.Parse(putBody).RootElement;
        S118ContractAssert.AssertExactKeySet(putRoot, ChildEntitlementKeys, "child PUT 200");
        Assert.Equal(childId, putRoot.GetProperty("configId").GetGuid()); // Case C: the SAME row
        Assert.Equal(2L, putRoot.GetProperty("version").GetInt64());
        Assert.Equal(3.0m, putRoot.GetProperty("annualQuota").GetDecimal()); // decimal fidelity on the edit
        Assert.True(putRoot.GetProperty("fullDayOnly").GetBoolean());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 12 — DELETE /api/agreement-configs/{configId}/entitlements/{id} (declared 204).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The child soft-DELETE: declared 204 = status + EMPTY body, both asserted by the
    /// matcher; admin-strict If-Match from the create ETag.</summary>
    [Fact]
    public async Task ChildEntitlements_Delete204_StatusAndEmptyBodyMatchRuntime()
    {
        using var admin = Admin();
        var (parentId, _, _) = await CreateConfigAsync(admin, "S118AGE_DEL");
        var (childId, etagVersion, _) = await CreateChildAsync(admin, parentId);

        await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Delete,
                $"/api/agreement-configs/{parentId}/entitlements/{childId}",
                jsonBody: null, ifMatchVersion: etagVersion),
            "/api/agreement-configs/{configId}/entitlements/{entitlementConfigId}", "delete");
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The RED-on-lie proof (the family's injected-lie demonstration).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The established injected-lie technique (S112/S117): the REAL create 201 body
    /// passes against the COMMITTED truth contract (GREEN), then the SAME body is matched
    /// against a corrupted copy of the spec whose <c>AgreementConfigResponse</c> schema gains a
    /// phantom <c>required</c> member — the matcher MUST go RED through the required-fidelity
    /// path with the phantom member NAMED. In-memory corruption ⇒ the committed spec on disk is
    /// never touched (revert-free by construction; the truth pass on the same response is the
    /// GREEN demonstration).</summary>
    [Fact]
    public async Task Gate_IsRed_OnInjectedPhantomRequiredMember_AndGreenOnTheCommittedTruth()
    {
        using var admin = Admin();
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/agreement-configs", ConfigRequestJson("S118AGC_RED")));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(201, (int)response.StatusCode);

        const string path = "/api/agreement-configs";

        // GREEN — the committed truth passes on the real 201.
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, path, "post");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 201, body, "truth");

        // RED — the same response against the spec with a phantom required member injected.
        var lieNode = JsonNode.Parse(_spec.GetRawText())!;
        var schema = lieNode["components"]!["schemas"]!["StatsTid.Backend.Api.Contracts.AgreementConfigResponse"]!;
        ((JsonArray)schema["required"]!).Add("s118PhantomMember");
        var lieSpec = JsonDocument.Parse(lieNode.ToJsonString()).RootElement.Clone();

        var lieContract = SpecRuntimeMatcher.ResolveSuccessContract(lieSpec, path, "post");
        var ex = Assert.Throws<XunitException>(() =>
            SpecRuntimeMatcher.AssertSuccessMatches(lieSpec, lieContract, 201, body, "injected-required-lie"));

        Assert.Contains("s118PhantomMember", ex.Message, StringComparison.Ordinal);
        Assert.Contains("REQUIRED", ex.Message, StringComparison.Ordinal); // the required-fidelity path, not a kind check
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient Admin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);

    /// <summary>Create a DRAFT config through the REAL endpoint; returns (configId,
    /// ETag-version, body). Throws with the response body on any non-201 (seed diagnostics).</summary>
    private async Task<(Guid ConfigId, long EtagVersion, JsonElement Body)> CreateConfigAsync(
        HttpClient client, string agreementCode, string normModel = "WEEKLY_HOURS")
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/agreement-configs", ConfigRequestJson(agreementCode, normModel)));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Config create for {agreementCode} returned {(int)response.StatusCode}: {body}");
        var root = JsonDocument.Parse(body).RootElement.Clone();
        return (root.GetProperty("configId").GetGuid(), S118ContractAssert.EtagVersion(response), root);
    }

    /// <summary>Create a CARE_DAY child entitlement under <paramref name="parentConfigId"/>
    /// through the REAL sub-resource POST; returns (childConfigId, ETag-version, body).</summary>
    private async Task<(Guid ChildId, long EtagVersion, JsonElement Body)> CreateChildAsync(
        HttpClient client, Guid parentConfigId)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{parentConfigId}/entitlements", ChildCreateJson));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Child entitlement create under {parentConfigId} returned {(int)response.StatusCode}: {body}");
        var root = JsonDocument.Parse(body).RootElement.Clone();
        return (root.GetProperty("configId").GetGuid(), S118ContractAssert.EtagVersion(response), root);
    }

    private static JsonElement FindByCode(JsonElement array, string agreementCode)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("agreementCode").GetString(), agreementCode, StringComparison.Ordinal)
                && string.Equals(el.GetProperty("okVersion").GetString(), OkVersion, StringComparison.Ordinal))
                return el;
        throw new XunitException($"Expected a row for ({agreementCode}, {OkVersion}) in: {array.GetRawText()}");
    }

    // ─────────────────────────────── request bodies (invariant JSON) ───────────────────────────────

    /// <summary>The full AgreementConfigRequest body (all C#-required members) — only the
    /// natural key + normModel vary per test.</summary>
    private static string ConfigRequestJson(string agreementCode, string normModel = "WEEKLY_HOURS")
        => $$"""
           {
             "agreementCode": "{{agreementCode}}", "okVersion": "{{OkVersion}}",
             "description": "S118 kontrakttest", "normModel": "{{normModel}}",
             "weeklyNormHours": 37.0, "normPeriodWeeks": 4, "annualNormHours": 1924.0,
             "maxFlexBalance": 74.0, "flexCarryoverMax": 37.0,
             "hasOvertime": true, "hasMerarbejde": false,
             "overtimeThreshold50": 37.0, "overtimeThreshold100": 44.0,
             "eveningSupplementEnabled": true, "nightSupplementEnabled": true,
             "weekendSupplementEnabled": true, "holidaySupplementEnabled": true,
             "eveningStart": 17, "eveningEnd": 23, "nightStart": 23, "nightEnd": 6,
             "eveningRate": 0.25, "nightRate": 0.50, "weekendSaturdayRate": 0.50,
             "weekendSundayRate": 1.00, "holidayRate": 1.00,
             "onCallDutyEnabled": true, "onCallDutyRate": 0.25,
             "callInWorkEnabled": true, "callInMinimumHours": 3.0, "callInRate": 1.50,
             "travelTimeEnabled": true, "workingTravelRate": 1.00, "nonWorkingTravelRate": 0.50
           }
           """;

    /// <summary>CARE_DAY child create — <c>fullDayOnly: true</c> is GUARD-FORCED (the S73 D-A
    /// ruling), which makes the ruling #2 pin non-vacuous. effectiveFrom omitted ⇒ today.</summary>
    private const string ChildCreateJson =
        """
        { "entitlementType": "CARE_DAY", "annualQuota": 2.0, "accrualModel": "IMMEDIATE",
          "resetMonth": 1, "carryoverMax": 0, "proRateByPartTime": true, "isPerEpisode": false,
          "description": "S118 omsorgsdage", "fullDayOnly": true }
        """;

    /// <summary>Child PUT — same-day (effectiveFrom = today, required by the validator);
    /// resetMonth/accrualModel unchanged (the immutability guard); quota edited 2.0 → 3.0.</summary>
    private static string ChildPutJson()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $$"""
               { "entitlementType": "CARE_DAY", "annualQuota": 3.0, "accrualModel": "IMMEDIATE",
                 "resetMonth": 1, "carryoverMax": 0, "proRateByPartTime": true, "isPerEpisode": false,
                 "description": "S118 omsorgsdage (redigeret)", "fullDayOnly": true,
                 "effectiveFrom": "{{today}}" }
               """;
    }
}
