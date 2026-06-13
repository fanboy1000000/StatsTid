using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S59 / TASK-5910 / ADR-029 — HTTP-level regression for the per-employee entitlement
/// eligibility enforcement on the Skema endpoints
/// (<c>GET /api/skema/{id}/month</c> display filter + <c>POST /api/skema/{id}/save</c>
/// authoritative gate), plus the HR admin authz on
/// <c>PUT /api/admin/employees/{id}/entitlement-eligibility/{type}</c>.
///
/// <para>
/// <b>Two mechanisms under test (refinement REVISION 1):</b>
/// <list type="bullet">
///   <item><description><b>Child-sick</b> — per-employee opt-in eligibility. Absent
///   eligibility row ⇒ ineligible (opt-in default): GET omits the CHILD_SICK_* types and
///   POST of a CHILD_SICK_DAY absence is rejected 422 <c>absence_type_not_eligible</c> with
///   nothing persisted. After HR grants eligibility (via the real admin PUT endpoint), the
///   same registration succeeds. The CHILD_SICK gate is a pure Backend fact-gate that
///   rejects BEFORE any rule-engine hop.</description></item>
///   <item><description><b>Senior</b> — DOB-derived age gate (no manual toggle). Validated
///   PER absence row via the rule engine: an employee whose 62nd birthday falls inside the
///   saved month has earlier-dated (age 61) SENIOR_DAY rows rejected and later-dated (age 62)
///   rows allowed in one save. An employee ≥62 throughout is allowed. An employee with NO
///   <c>birth_date</c> is rejected (fail-closed) and GET hides SENIOR_DAY.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Rule-engine wiring.</b> The SENIOR_DAY POST gate calls the rule engine over HTTP
/// (<c>POST /api/rules/validate-entitlement</c>). The CHILD_SICK gate does not. The
/// in-process <see cref="StatsTidWebApplicationFactory"/> harness has no rule-engine
/// container, so we replace <see cref="IHttpClientFactory"/> with a stub whose handler
/// drives the REAL <see cref="EntitlementValidationRule.Evaluate"/> (the same rule the
/// containerised engine runs) — the age-gate verdict is authentic, not a re-implemented
/// mirror. Mirrors the rule-engine-stub pattern in <see cref="TestFixtures"/>.
/// </para>
///
/// <para>
/// HTTP-level WAF&lt;Program&gt; harness + seeded employee <c>emp001</c> (STY01, AC, OK24)
/// and token-mint pattern from <see cref="SkemaWorkTimeDateRangeGuardTests"/>. DB facts
/// (eligibility rows, <c>birth_date</c>) are seeded directly in setup via the harness's
/// direct DB access; assertions read <c>absences_projection</c> back from the DB. Docker-
/// gated fixture + trait match the sibling guard tests.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaEntitlementEligibilityGuardTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // Seeded employee (init.sql L871): emp001, STY01, AC, OK24. Self-save → actor == route id.
    private const string Emp001 = "emp001";
    private const string Emp001OrgId = "STY01";

    // HR scoped MIN01 ⊇ STY01 (init.sql hr01: LOCAL_HR, MIN01, ORG_AND_DESCENDANTS) → in-scope
    // for emp001. hr02 is scoped MIN02 (disjoint) → cross-org reject.
    private const string Hr01 = "hr01";
    private const string Hr01ScopeOrg = "MIN01";
    private const string Hr02 = "hr02";
    private const string Hr02ScopeOrg = "MIN02";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // Build a client whose IHttpClientFactory is replaced by a stub driving the real
    // EntitlementValidationRule for /api/rules/validate-entitlement (senior age gate).
    private HttpClient CreateRuleStubbedClient()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory());
            });
        });
        return factory.CreateClient();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Child-sick — opt-in (absent row ⇒ ineligible): GET hides, POST 422, grant then succeed.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// No eligibility row for emp001 (opt-in default): GET <c>/month</c> omits ALL child-sick
    /// types (entitlement_type CHILD_SICK). Other types remain visible.
    /// </summary>
    [Fact]
    public async Task ChildSick_NoEligibilityRow_GetMonth_OmitsChildSickTypes()
    {
        var client = EmployeeClient();

        var types = await GetMonthAbsenceTypesAsync(client, 2026, 3);

        Assert.DoesNotContain("CHILD_SICK_DAY", types);
        // Sanity: an unrelated, always-available type is still present.
        Assert.Contains("VACATION", types);
    }

    /// <summary>
    /// No eligibility row: POST of a CHILD_SICK_DAY absence → 422
    /// <c>absence_type_not_eligible</c>, nothing persisted.
    /// </summary>
    [Fact]
    public async Task ChildSick_NoEligibilityRow_PostSave_Returns422_AndPersistsNothing()
    {
        var client = EmployeeClient();
        var date = new DateOnly(2026, 3, 10);

        var rsp = await PostAbsenceAsync(client, date, "CHILD_SICK_DAY");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_type_not_eligible", body.GetProperty("error").GetString());
        Assert.Equal("CHILD_SICK", body.GetProperty("entitlementType").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, date));
    }

    /// <summary>
    /// Grant CHILD_SICK eligibility via the REAL admin PUT (HR, in-scope), then the same
    /// registration that 422'd now succeeds and persists. Proves the GET-hide / POST-422 is
    /// driven by the eligibility projection the admin endpoint writes — not a hardcoded deny.
    ///
    /// <para>
    /// The admin endpoint server-stamps <c>effective_from = today (UTC)</c> (ADR-023 D8,
    /// forward-only), and both the GET filter (as-of month-end) and the POST gate (as-of
    /// absence.Date) are DATED reads — so the grant only takes effect for dates on/after
    /// today. This test therefore exercises the CURRENT month (its month-end ≥ today) and a
    /// save date ≥ today, which is the realistic "HR grants, employee registers" flow.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ChildSick_AfterHrGrantsEligibility_PostSave_Succeeds_AndPersists()
    {
        // Current month (month-end ≥ today) so the grant's effective_from=today covers the
        // GET month-end anchor and the save date.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // ADR-032 D3 (S66): entitlement-consuming absences on zero-norm days (weekends) are
        // now rejected 422 — book the next weekday ON OR AFTER today. Coverage is preserved
        // (eligibility row is effective_from=today, open-ended ⇒ any date ≥ today is covered);
        // on weekdays this stays the exact as-of == effective_from boundary the test pins.
        var saveDate = today.DayOfWeek switch
        {
            DayOfWeek.Saturday => today.AddDays(2),
            DayOfWeek.Sunday => today.AddDays(1),
            _ => today,
        };
        var year = saveDate.Year;
        var month = saveDate.Month;

        // Rule-stubbed client: once the eligibility gate passes, the CHILD_SICK POST still
        // runs quota validation through the rule engine (HTTP), so the stub is required for
        // the successful save. The eligibility 422 itself rejects pre-rule-engine.
        var employee = CreateEmployeeClient(CreateRuleStubbedClient());
        var pre = await PostAbsenceAsync(employee, saveDate, "CHILD_SICK_DAY");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pre.StatusCode);

        // HR grants eligibility (first-create ⇒ If-None-Match: *).
        var hr = HrClient(Hr01, Hr01ScopeOrg);
        var grant = await PutEligibilityAsync(hr, Emp001, "CHILD_SICK", eligible: true, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        // Now GET offers the type (as-of this month's month-end ≥ today) and POST succeeds.
        var types = await GetMonthAbsenceTypesAsync(employee, year, month);
        Assert.Contains("CHILD_SICK_DAY", types);

        var save = await PostAbsenceAsync(employee, saveDate, "CHILD_SICK_DAY");
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, saveDate));
    }

    /// <summary>
    /// Absent-row parity: for the SAME ineligible CHILD_SICK case, GET hides the type AND POST
    /// rejects it. The two enforcement points agree on the absent-row default (ineligible).
    /// </summary>
    [Fact]
    public async Task ChildSick_AbsentRow_GetHide_And_PostReject_Parity()
    {
        var client = EmployeeClient();

        var types = await GetMonthAbsenceTypesAsync(client, 2026, 4);
        var getHidden = !types.Contains("CHILD_SICK_DAY");

        var rsp = await PostAbsenceAsync(client, new DateOnly(2026, 4, 8), "CHILD_SICK_DAY");
        var postRejected = rsp.StatusCode == HttpStatusCode.UnprocessableEntity;

        Assert.True(getHidden, "GET should hide CHILD_SICK for the absent-row (ineligible) case.");
        Assert.True(postRejected, "POST should reject CHILD_SICK for the absent-row (ineligible) case.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Senior — DOB-derived per-row age gate (rule engine over HTTP).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Birthday boundary: emp001's DOB makes them 61 on 2026-03-05 but 62 from 2026-03-20.
    /// A single POST with both SENIOR_DAY rows must reject the under-age (earlier) row 422
    /// and persist NOTHING (atomic pre-tx reject on the first failing row). Proves per-row
    /// age-as-of-date validation across a birthday within one month.
    /// </summary>
    [Fact]
    public async Task Senior_BirthdayBoundary_UnderAgeRowRejected_NothingPersisted()
    {
        // 62nd birthday on 2026-03-20 → 61 on the 5th, 62 on the 25th.
        await SetBirthDateAsync(Emp001, new DateOnly(1964, 3, 20));

        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        var earlyUnderAge = new DateOnly(2026, 3, 5);   // age 61 → reject
        var lateEligible = new DateOnly(2026, 3, 25);   // age 62 → would pass

        var rsp = await PostAbsencesAsync(client, 2026, 3, new[]
        {
            (earlyUnderAge, "SENIOR_DAY"),
            (lateEligible, "SENIOR_DAY"),
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_type_not_eligible", body.GetProperty("error").GetString());
        Assert.Equal("SENIOR_DAY", body.GetProperty("entitlementType").GetString());
        // The named offending date is the under-age (earlier) row.
        Assert.Equal(earlyUnderAge, body.GetProperty("date").GetDateTime().ToDateOnly());

        // Atomic: nothing persisted (the gate runs pre-transaction).
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, earlyUnderAge));
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, lateEligible));
    }

    /// <summary>
    /// Employee ≥62 throughout the month: a SENIOR_DAY row is allowed and persists.
    /// </summary>
    [Fact]
    public async Task Senior_EmployeeOver62Throughout_PostSave_Succeeds_AndPersists()
    {
        await SetBirthDateAsync(Emp001, new DateOnly(1960, 1, 1)); // 66 in 2026

        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        // 2026-03-16 is a Monday — 2026-03-14 (Saturday) now 422s per ADR-032 D3 (S66):
        // entitlement-consuming absences on zero-norm days are rejected.
        var date = new DateOnly(2026, 3, 16);

        var rsp = await PostAbsencesAsync(client, 2026, 3, new[] { (date, "SENIOR_DAY") });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(1, await CountAbsenceRowsAsync(Emp001, date));
    }

    /// <summary>
    /// No <c>birth_date</c> (null DOB): SENIOR_DAY POST is rejected fail-closed (422) and GET
    /// hides SENIOR_DAY. emp001 is seeded with NO birth_date, so we assert directly.
    /// </summary>
    [Fact]
    public async Task Senior_NullDob_PostRejectedFailClosed_AndGetHidesSenior()
    {
        // emp001 seeded with no birth_date — leave it null (do not set).
        var client = CreateEmployeeClient(CreateRuleStubbedClient());
        var date = new DateOnly(2026, 3, 11);

        // POST fail-closed.
        var rsp = await PostAbsencesAsync(client, 2026, 3, new[] { (date, "SENIOR_DAY") });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("absence_type_not_eligible", body.GetProperty("error").GetString());
        Assert.Equal("SENIOR_DAY", body.GetProperty("entitlementType").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(Emp001, date));

        // GET hides SENIOR_DAY for the no-DOB employee (parity with the POST reject).
        var types = await GetMonthAbsenceTypesAsync(client, 2026, 3);
        Assert.DoesNotContain("SENIOR_DAY", types);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Authz — eligibility write requires HROrAbove + in-scope; cross-org rejected.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>A non-HR caller (Employee) cannot set eligibility → 401/403 (policy denial).</summary>
    [Fact]
    public async Task EligibilityWrite_NonHrCaller_IsForbidden()
    {
        var employee = EmployeeClient(); // Employee role — fails HROrAbove policy
        var rsp = await PutEligibilityAsync(employee, Emp001, "CHILD_SICK", eligible: true, ifNoneMatchStar: true);

        Assert.True(
            rsp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 401/403 for a non-HR caller, got {(int)rsp.StatusCode}.");
        // Nothing granted.
        Assert.False(await EligibilityRowExistsAsync(Emp001, "CHILD_SICK"));
    }

    /// <summary>
    /// HR scoped to a disjoint org (hr02 @ MIN02) cannot set eligibility for emp001 (MIN01
    /// subtree) → 403 (OrgScopeValidator cross-org guard). Passes the HROrAbove policy but
    /// fails the explicit scope binding.
    /// </summary>
    [Fact]
    public async Task EligibilityWrite_CrossOrgHr_IsForbidden()
    {
        var crossOrgHr = HrClient(Hr02, Hr02ScopeOrg);
        var rsp = await PutEligibilityAsync(crossOrgHr, Emp001, "CHILD_SICK", eligible: true, ifNoneMatchStar: true);

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        Assert.False(await EligibilityRowExistsAsync(Emp001, "CHILD_SICK"));
    }

    /// <summary>
    /// Scope guard: HR cannot set SENIOR_DAY (or any non-CHILD_SICK type) via the eligibility
    /// endpoint — senior is fully age-derived (refinement line 117) → 422.
    /// </summary>
    [Fact]
    public async Task EligibilityWrite_NonChildSickType_Rejected422()
    {
        var hr = HrClient(Hr01, Hr01ScopeOrg);
        var rsp = await PutEligibilityAsync(hr, Emp001, "SENIOR_DAY", eligible: true, ifNoneMatchStar: true);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        Assert.False(await EligibilityRowExistsAsync(Emp001, "SENIOR_DAY"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Conditional-write guard (S59 Step-7a BLOCKER 1) — If-None-Match: * is create-only;
    // a blind re-create against a live row is a 409 (no lost update); toggles use If-Match.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lost-update prevention. First-create succeeds (If-None-Match: *). A SECOND blind
    /// If-None-Match: * against the now-existing live row returns 409 Conflict and does NOT
    /// overwrite (the eligibility value is unchanged). The correct read-then-If-Match flow with
    /// the current version succeeds (toggles eligible→false, bumping the version). An If-Match
    /// carrying a STALE version returns 412. Proves the create-only path can never blind-clobber
    /// an HR-set value and that the optimistic-concurrency toggle is enforced.
    /// </summary>
    [Fact]
    public async Task EligibilityWrite_BlindRecreateOnLiveRow_Returns409_IfMatchEnforcesVersion()
    {
        var hr = HrClient(Hr01, Hr01ScopeOrg);

        // First-create (no live row) via If-None-Match: * → 200, version 1.
        var create = await PutEligibilityAsync(hr, Emp001, "CHILD_SICK", eligible: true, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var v1 = await ReadVersionAsync(create);

        // Second blind If-None-Match: * against the EXISTING live row → 409 (create-only),
        // and the stored value is untouched.
        var blind = await PutEligibilityAsync(hr, Emp001, "CHILD_SICK", eligible: false, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.Conflict, blind.StatusCode);
        var blindBody = await blind.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(v1, blindBody.GetProperty("currentVersion").GetInt64());
        // No overwrite: still eligible=true at version 1.
        Assert.True(await EligibleNowAsync(Emp001, "CHILD_SICK"));

        // Correct read-then-If-Match flow with the current version → 200, version bumps.
        var toggle = await PutEligibilityIfMatchAsync(hr, Emp001, "CHILD_SICK", eligible: false, version: v1);
        Assert.Equal(HttpStatusCode.OK, toggle.StatusCode);
        var v2 = await ReadVersionAsync(toggle);
        Assert.True(v2 > v1);
        Assert.False(await EligibleNowAsync(Emp001, "CHILD_SICK"));

        // A stale If-Match (the now-superseded v1) → 412.
        var stale = await PutEligibilityIfMatchAsync(hr, Emp001, "CHILD_SICK", eligible: true, version: v1);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        // Still false (the stale write did not land).
        Assert.False(await EligibleNowAsync(Emp001, "CHILD_SICK"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET eligibility (S59 Step-7a BLOCKER 1) — read-then-If-Match support surface.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Before any set: GET returns <c>rowExists:false</c> + <c>eligible:false</c> (absent-row
    /// opt-in default) and NO ETag (signals the client to create with If-None-Match: *). After
    /// HR sets eligibility, GET returns the live state + an ETag whose version matches the body
    /// version (the read-then-If-Match handshake). The returned version round-trips into a
    /// successful If-Match toggle.
    /// </summary>
    [Fact]
    public async Task GetEligibility_BeforeSet_RowExistsFalse_NoEtag_AfterSet_StateAndEtag()
    {
        var hr = HrClient(Hr01, Hr01ScopeOrg);

        // Before any set — absent-row default, no ETag.
        var before = await GetEligibilityAsync(hr, Emp001, "CHILD_SICK");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var beforeBody = await before.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(beforeBody.GetProperty("rowExists").GetBoolean());
        Assert.False(beforeBody.GetProperty("eligible").GetBoolean());
        Assert.Null(before.Headers.ETag); // no ETag before a row exists
        Assert.False(beforeBody.TryGetProperty("version", out _));

        // HR grants — first-create (If-None-Match: *).
        var grant = await PutEligibilityAsync(hr, Emp001, "CHILD_SICK", eligible: true, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        var grantedVersion = await ReadVersionAsync(grant);

        // After the set — live state + ETag matching the body version.
        var after = await GetEligibilityAsync(hr, Emp001, "CHILD_SICK");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var afterBody = await after.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(afterBody.GetProperty("rowExists").GetBoolean());
        Assert.True(afterBody.GetProperty("eligible").GetBoolean());
        var bodyVersion = afterBody.GetProperty("version").GetInt64();
        Assert.Equal(grantedVersion, bodyVersion);
        Assert.NotNull(after.Headers.ETag);
        Assert.Equal($"\"{bodyVersion}\"", after.Headers.ETag!.Tag);

        // The GET-supplied version composes a coherent If-Match toggle.
        var toggle = await PutEligibilityIfMatchAsync(hr, Emp001, "CHILD_SICK", eligible: false, version: bodyVersion);
        Assert.Equal(HttpStatusCode.OK, toggle.StatusCode);
    }

    /// <summary>
    /// GET eligibility is HROrAbove + cross-org bound (mirrors the PUT, FAIL-001). A non-HR
    /// caller (Employee) is 401/403; HR scoped to a disjoint org (hr02 @ MIN02) reading emp001
    /// (MIN01 subtree) is 403 (OrgScopeValidator). An in-scope HR succeeds.
    /// </summary>
    [Fact]
    public async Task GetEligibility_NonHr_And_CrossOrgHr_AreForbidden_InScopeAllowed()
    {
        // Non-HR (Employee) — fails the HROrAbove policy.
        var employee = EmployeeClient();
        var empRsp = await GetEligibilityAsync(employee, Emp001, "CHILD_SICK");
        Assert.True(
            empRsp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Expected 401/403 for a non-HR GET, got {(int)empRsp.StatusCode}.");

        // Cross-org HR (hr02 @ MIN02) — passes the policy, fails the scope binding → 403.
        var crossOrgHr = HrClient(Hr02, Hr02ScopeOrg);
        var crossRsp = await GetEligibilityAsync(crossOrgHr, Emp001, "CHILD_SICK");
        Assert.Equal(HttpStatusCode.Forbidden, crossRsp.StatusCode);

        // In-scope HR (hr01 @ MIN01 ⊇ STY01) — allowed.
        var inScopeHr = HrClient(Hr01, Hr01ScopeOrg);
        var okRsp = await GetEligibilityAsync(inScopeHr, Emp001, "CHILD_SICK");
        Assert.Equal(HttpStatusCode.OK, okRsp.StatusCode);
    }

    // Senior as-of-date min_age (S59 Step-7a BLOCKER 2): the senior gate resolves the
    // SENIOR_DAY config (min_age) via GetByTypeAtAsync at THIS row's absence date (POST) /
    // month-end (GET), so age and min_age share one anchor. min_age is uniformly 62 across all
    // dated configs today, so a dedicated dated-divergence case would require seeding a
    // second-dated SENIOR_DAY config (out of this regression's seed scope). The dated read is
    // already exercised by Senior_BirthdayBoundary_UnderAgeRowRejected_NothingPersisted (which
    // crosses the 62nd-birthday boundary mid-month against the as-of-date-resolved min_age) and
    // the other senior cases above — so no separate min_age case is added here.

    // ── HTTP helpers ──

    private HttpClient EmployeeClient() => CreateEmployeeClient(_factory.CreateClient());

    private static HttpClient CreateEmployeeClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintEmployeeToken(Emp001, Emp001OrgId));
        return client;
    }

    private HttpClient HrClient(string hrId, string scopeOrg)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintHrToken(hrId, scopeOrg));
        return client;
    }

    private static async Task<HashSet<string>> GetMonthAbsenceTypesAsync(HttpClient client, int year, int month)
    {
        var rsp = await client.GetAsync($"/api/skema/{Emp001}/month?year={year}&month={month}");
        rsp.EnsureSuccessStatusCode();
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("absenceTypes").EnumerateArray()
            .Select(e => e.GetProperty("type").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Task<HttpResponseMessage> PostAbsenceAsync(HttpClient client, DateOnly date, string absenceType)
        => PostAbsencesAsync(client, date.Year, date.Month, new[] { (date, absenceType) });

    private static async Task<HttpResponseMessage> PostAbsencesAsync(
        HttpClient client, int year, int month, (DateOnly Date, string Type)[] absences)
    {
        var request = new
        {
            year,
            month,
            absences = absences.Select(a => new
            {
                date = a.Date.ToString("yyyy-MM-dd"),
                absenceType = a.Type,
                hours = 7.4m,
            }).ToArray(),
        };
        return await client.PostAsJsonAsync($"/api/skema/{Emp001}/save", request);
    }

    private static async Task<HttpResponseMessage> PutEligibilityAsync(
        HttpClient client, string employeeId, string entitlementType, bool eligible, bool ifNoneMatchStar)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { eligible }), Encoding.UTF8, "application/json"),
        };
        if (ifNoneMatchStar)
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return await client.SendAsync(req);
    }

    /// <summary>PUT with an <c>If-Match: "&lt;version&gt;"</c> precondition (toggle existing).</summary>
    private static async Task<HttpResponseMessage> PutEligibilityIfMatchAsync(
        HttpClient client, string employeeId, string entitlementType, bool eligible, long version)
    {
        var req = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { eligible }), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await client.SendAsync(req);
    }

    private static Task<HttpResponseMessage> GetEligibilityAsync(
        HttpClient client, string employeeId, string entitlementType)
        => client.GetAsync($"/api/admin/employees/{employeeId}/entitlement-eligibility/{entitlementType}");

    /// <summary>Reads the numeric version out of the response body's <c>version</c> field.</summary>
    private static async Task<long> ReadVersionAsync(HttpResponseMessage rsp)
    {
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("version").GetInt64();
    }

    // ── DB helpers (direct access via the harness) ──

    private async Task SetBirthDateAsync(string employeeId, DateOnly birthDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET birth_date = @b WHERE user_id = @e", conn);
        cmd.Parameters.AddWithValue("b", birthDate);
        cmd.Parameters.AddWithValue("e", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountAbsenceRowsAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @e AND date = @d",
            conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<bool> EligibilityRowExistsAsync(string employeeId, string entitlementType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM employee_entitlement_eligibility
            WHERE employee_id = @e AND entitlement_type = @t
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    /// <summary>Reads the live (effective_to IS NULL) row's <c>eligible</c> flag directly.</summary>
    private async Task<bool> EligibleNowAsync(string employeeId, string entitlementType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT eligible FROM employee_entitlement_eligibility
            WHERE employee_id = @e AND entitlement_type = @t AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result is DBNull)
            throw new InvalidOperationException(
                $"No live eligibility row for {employeeId}/{entitlementType}.");
        return (bool)result;
    }

    // ── Token minting (mirrors SkemaWorkTimeDateRangeGuardTests) ──

    private static string MintEmployeeToken(string actorId, string orgId)
    {
        var tokenService = new JwtTokenService(DevSettings());
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "AC",
            orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static string MintHrToken(string actorId, string scopeOrgId)
    {
        var tokenService = new JwtTokenService(DevSettings());
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.LocalHR,
            agreementCode: "AC",
            orgId: scopeOrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, scopeOrgId, "ORG_AND_DESCENDANTS") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ── Rule-engine stub: drives the REAL EntitlementValidationRule over the HTTP seam ──

    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        // S73 / TASK-7300 (R1): the endpoints now resolve the NAMED RuleEngine client and use
        // RELATIVE request URIs; this stub replaces the whole factory, so it supplies the
        // BaseAddress the production registration sets (behavior-preserving fixture change).
        public HttpClient CreateClient(string name) => new(new RuleEngineStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }

    /// <summary>
    /// Stub handler for <c>POST /api/rules/validate-entitlement</c>. Deserializes the
    /// request the Backend sends (camelCase), runs the REAL
    /// <see cref="EntitlementValidationRule.Evaluate"/>, and returns the response — so the
    /// senior age-gate verdict is the genuine rule-engine logic, not a re-implemented mirror.
    /// </summary>
    private sealed class RuleEngineStubHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/api/rules/validate-entitlement", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var req = JsonSerializer.Deserialize<ValidateEntitlementRequest>(json, Camel)!;
            var result = EntitlementValidationRule.Evaluate(req);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(result, Camel), Encoding.UTF8, "application/json"),
            };
        }
    }
}

internal static class JsonDateOnlyExtensions
{
    /// <summary>The Skema endpoints serialize <c>DateOnly</c> as an ISO date string; the
    /// JSON reader surfaces it as a <see cref="DateTime"/> via GetDateTime. Convert back.</summary>
    public static DateOnly ToDateOnly(this DateTime dt) => DateOnly.FromDateTime(dt);
}
