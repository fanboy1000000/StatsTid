using System.Net;
using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// SEC-032 — the position-override WRITE endpoints are a GLOBAL config surface (one row governs
/// EVERY institution on that agreement+position; the table has no org column and resolution ignores
/// org). Owner ruling OQ-1(a): the FOUR writes (create / update / deactivate / activate) are floored
/// at <c>GlobalAdminOnly</c>, matching the sibling global-config surfaces (AgreementConfig,
/// EntitlementConfig) — so a LocalAdmin at one institution can no longer rewrite norms every
/// institution inherits (a cross-tenant elevation). Owner ruling OQ-2: the THREE GET reads STAY at
/// <c>LocalAdminOrAbove</c> (view-only transparency; no elevation).
///
/// <para><b>What these tests pin (both review lenses required them):</b></para>
/// <list type="number">
///   <item><description><b>Negative — all four writes reject LocalAdmin (403).</b> Each write has
///     its OWN independent <c>.RequireAuthorization("GlobalAdminOnly")</c>, so each is asserted
///     SEPARATELY (a subset check could pass while one endpoint was silently left open). The
///     LocalAdmin token is OTHERWISE-VALID (correct signing key / iss / aud + a real ORG_ONLY
///     scope), so the 403 is the ROLE policy — not an auth failure. On the OLD
///     <c>LocalAdminOrAbove</c> floor these were 200/201, so this is RED-before-green.</description></item>
///   <item><description><b>Positive control — GlobalAdmin still runs the full write lifecycle</b>
///     (create 201 → update 200 → deactivate 200 → activate 200): the fix did NOT lock out the
///     legitimate admin. Every one of the four writes has a green GlobalAdmin path here (and the
///     existing <c>S118PositionOverrideSpecRuntimeTests</c> GlobalAdmin happy-paths cover them
///     too).</description></item>
///   <item><description><b>Positive read-floor lock — LocalAdmin still reads (200).</b> Locks the
///     OQ-2 "reads stay open" decision so a later blanket "tighten everything" pass cannot silently
///     flip the GET reads to GlobalAdminOnly without turning this test RED.</description></item>
/// </list>
///
/// <para>Fresh testcontainer per test (Docker-gated); the LocalAdmin/GlobalAdmin clients are minted
/// by <see cref="SpecRuntimeTestSupport"/> (same signing key / iss / aud as the S118 suite). The
/// four init.sql seed overrides are never mutated — the GlobalAdmin lifecycle drives its OWN row
/// under a <c>SEC032_*</c> agreement code on the test-seeded position <c>SEC032_POS</c>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SEC032PositionOverrideAuthorizationTests : IAsyncLifetime
{
    private const string GlobalAdminActor = "sec032_gadmin";
    private const string LocalAdminActor = "sec032_ladmin";
    private const string LocalAdminOrg = "STY01"; // the LocalAdmin's real ORG_ONLY scope (passes the read floor)
    private const string OkVersion = "OKSEC032";
    private const string PositionCode = "SEC032_POS"; // test-seeded positions row (FK target)

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders

        // INPUT seed only: the FK target for this suite's own override rows. The 4 init.sql
        // override seeds are left untouched.
        await ExecAsync(
            """
            INSERT INTO positions (position_code, display_label, agreement_code)
            VALUES ('SEC032_POS', 'SEC-032 Testposition', 'AC')
            ON CONFLICT DO NOTHING
            """);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Negative — a valid LocalAdmin token is 403 on EACH of the four writes.
    //  Authorization runs before the endpoint delegate, so the request short-circuits to
    //  403 on the ROLE policy regardless of the request particulars — the non-existent
    //  {overrideId} and the dummy If-Match version (v1, supplied on update/deactivate/
    //  activate) are never reached, so the result is 403, never 401/404/412/428.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Write #1 — POST create. On the old LocalAdminOrAbove floor this was 201.</summary>
    [Fact]
    public async Task Create_Post_LocalAdmin_Forbidden403()
    {
        using var localAdmin = LocalAdmin();
        using var response = await localAdmin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/position-overrides", CreateJson("SEC032_CRT", weeklyNormHours: "null")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Write #2 — PUT update. On the old floor this was 200.</summary>
    [Fact]
    public async Task Update_Put_LocalAdmin_Forbidden403()
    {
        using var localAdmin = LocalAdmin();
        using var response = await localAdmin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/admin/position-overrides/{Guid.NewGuid()}",
            CreateJson("SEC032_UPD", weeklyNormHours: "36.75"), ifMatchVersion: 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Write #3 — POST deactivate. On the old floor this was 200.</summary>
    [Fact]
    public async Task Deactivate_Post_LocalAdmin_Forbidden403()
    {
        using var localAdmin = LocalAdmin();
        using var response = await localAdmin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/admin/position-overrides/{Guid.NewGuid()}/deactivate",
            jsonBody: null, ifMatchVersion: 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Write #4 — POST activate. On the old floor this was 200.</summary>
    [Fact]
    public async Task Activate_Post_LocalAdmin_Forbidden403()
    {
        using var localAdmin = LocalAdmin();
        using var response = await localAdmin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/admin/position-overrides/{Guid.NewGuid()}/activate",
            jsonBody: null, ifMatchVersion: 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Positive control — GlobalAdmin still succeeds on ALL FOUR writes (the fix did not
    //  lock out the legitimate admin). One row, full lifecycle, If-Match composed FE-style.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GlobalAdmin_StillRunsFullWriteLifecycle_AllFourWritesGreen()
    {
        using var admin = GlobalAdmin();

        // Write #1 — create (v1, ACTIVE).
        using var create = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/position-overrides", CreateJson("SEC032_LIFE", weeklyNormHours: "null")));
        var createBody = await create.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Created, create.StatusCode); // 201
        var overrideId = JsonDocument.Parse(createBody).RootElement.GetProperty("overrideId").GetGuid();
        var v1 = S118ContractAssert.EtagVersion(create);
        Assert.Equal(1L, v1);

        // Write #2 — update (If-Match 1 ⇒ v2, still ACTIVE).
        using var update = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/admin/position-overrides/{overrideId}",
            CreateJson("SEC032_LIFE", weeklyNormHours: "36.75"), ifMatchVersion: v1));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode); // 200
        var v2 = S118ContractAssert.EtagVersion(update);
        Assert.Equal(2L, v2);

        // Write #3 — deactivate (If-Match 2 ⇒ v3, INACTIVE).
        using var deactivate = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/deactivate",
            jsonBody: null, ifMatchVersion: v2));
        var deactivateBody = await deactivate.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode); // 200
        Assert.Equal("INACTIVE", JsonDocument.Parse(deactivateBody).RootElement.GetProperty("status").GetString());
        var v3 = S118ContractAssert.EtagVersion(deactivate);
        Assert.Equal(3L, v3);

        // Write #4 — activate (If-Match 3 ⇒ v4, back to ACTIVE).
        using var activate = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/activate",
            jsonBody: null, ifMatchVersion: v3));
        var activateBody = await activate.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode); // 200
        Assert.Equal("ACTIVE", JsonDocument.Parse(activateBody).RootElement.GetProperty("status").GetString());
        Assert.Equal(4L, S118ContractAssert.EtagVersion(activate));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Positive read-floor lock — LocalAdmin still reads (200). Guards OQ-2.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The list GET stays at LocalAdminOrAbove — a valid LocalAdmin token reads it (200).
    /// If a future blanket tightening flipped the reads to GlobalAdminOnly this turns RED.</summary>
    [Fact]
    public async Task ListRead_Get_LocalAdmin_Ok200_ReadFloorStaysOpen()
    {
        using var localAdmin = LocalAdmin();
        using var response = await localAdmin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Get, "/api/admin/position-overrides"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient GlobalAdmin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, GlobalAdminActor, LocalAdminOrg);

    private HttpClient LocalAdmin()
        => SpecRuntimeTestSupport.CreateLocalAdminClient(_factory, LocalAdminActor, LocalAdminOrg);

    private async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Create/update body — the same 8-member request shape serves both verbs.
    /// <paramref name="weeklyNormHours"/> is passed as raw JSON ("null" or a literal).</summary>
    private static string CreateJson(string agreementCode, string weeklyNormHours)
        => $$"""
           { "agreementCode": "{{agreementCode}}", "okVersion": "{{OkVersion}}",
             "positionCode": "{{PositionCode}}",
             "maxFlexBalance": 120.5, "flexCarryoverMax": 40.25, "normPeriodWeeks": 4,
             "weeklyNormHours": {{weeklyNormHours}}, "description": "SEC-032 positionsundtagelse" }
           """;
}
