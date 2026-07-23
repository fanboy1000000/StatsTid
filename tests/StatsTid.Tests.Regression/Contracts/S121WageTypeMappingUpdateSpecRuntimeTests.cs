using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S121 / TASK-12102 — the success-path proof for the WAGE-TYPE-MAPPING UPDATE flow, the
/// S118-deferred prod defect #1: the FE never sent the then-spec-required <c>effectiveFrom</c>,
/// so EVERY live update 400'd at the binder — the flow was DEAD in prod (only test suites,
/// which sent the member explicitly, ever exercised it). Owner ruling #1 (2026-07-23) made
/// PUT <c>effectiveFrom</c> OPTIONAL and server-defaulted to today (compute-once), retaining
/// the S29 same-day 422 for explicitly-sent non-today values.
///
/// <para><b>What this class pins:</b></para>
/// <list type="bullet">
///   <item><description><b>Ruling #1 omission-success on BOTH repo-dispatch branches:</b> the
///     PUT body OMITS <c>effectiveFrom</c> and succeeds — same-day in-place (predecessor
///     created today ⇒ version bump + ETag + UPDATED audit) AND cross-day supersede (the
///     predecessor SQL-backdated to yesterday ⇒ close + INSERT at version 1 + SUPERSEDED
///     audit). The branch discriminator is driven purely by predecessor age — nothing
///     downstream distinguishes server-defaulted today from a client-sent today.</description></item>
///   <item><description><b>The RETAINED validator:</b> an explicitly-sent non-today
///     <c>effectiveFrom</c> still 422s with the <c>suppliedEffectiveFrom</c>/<c>today</c>
///     echo (S29 semantics unchanged), and nothing mutates.</description></item>
/// </list>
///
/// <para><b>Seed discipline:</b> a FRESH testcontainer per test (the established S118 harness
/// conventions; matcher + Support consumed AS-IS); every natural key is
/// (<c>S121_TT_*</c>, <c>OKS121</c>, <c>S121WTM</c>, position "") with wage types
/// <c>SLS_S121*</c> — DISJOINT from the init.sql seed families (AC/HK/PROSA × OK24/OK26),
/// from the S118 gate keys (<c>S118_TT_*</c>/<c>OKS118</c>/<c>S118WTM</c>), and from all
/// pre-existing WTM suites (<c>WK_*</c>, <c>FR_WTM_*</c>, <c>WTM_S29_*</c>, the
/// CASEB/CASEC/CROSSDAY/SAMEDAY families). The cross-day predecessor age is seeded via SQL
/// backdate (the only way to age a row inside a fresh container).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S121WageTypeMappingUpdateSpecRuntimeTests : IAsyncLifetime
{
    private const string ActorId = "s121w_gadmin";
    private const string JwtOrg = "S121WM"; // JWT claim only — WTM audit rows are GLOBAL (no org FK)
    private const string AgreementCode = "S121WTM";
    private const string OkVersion = "OKS121";

    /// <summary>The EXACT 7 camelCase members of <c>WageTypeMappingResponse</c>.</summary>
    private static readonly string[] EntityKeys =
    {
        "timeType", "wageType", "okVersion", "agreementCode", "position", "description", "version",
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
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Branch 1 — same-day in-place: create today, PUT again today WITHOUT effectiveFrom.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The ruling #1 pin, same-day branch: the PUT body OMITS <c>effectiveFrom</c>
    /// (the exact shape the graduated FE hook now sends) and the update succeeds in place —
    /// matcher-asserted 200, version 1 → 2, ETag "2", ONE row for the key, and the UPDATED
    /// audit row LANDS with the (1 → 2) version-transition pair.</summary>
    [Fact]
    public async Task Update_Put200_SameDayInPlace_EffectiveFromOmitted_VersionBumpsAndUpdatedAuditLands()
    {
        using var admin = Admin();
        var etag = await CreateAsync(admin, "S121_TT_SAME");
        Assert.Equal(1L, etag);

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, "/api/admin/wage-type-mappings",
            PutJsonOmittingEffectiveFrom("S121_TT_SAME"), ifMatchVersion: etag));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode);
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/wage-type-mappings", "put");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "PUT /api/admin/wage-type-mappings (200, same-day)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, EntityKeys, "WTM PUT 200 (same-day in-place)");
        Assert.Equal("SLS_S121B", root.GetProperty("wageType").GetString()); // the edit landed
        Assert.Equal(2L, root.GetProperty("version").GetInt64());            // in-place bump
        Assert.Equal(2L, S118ContractAssert.EtagVersion(response));

        var today = Today();
        // ONE row for the key (no supersession insert), open, effective_from = today.
        Assert.Equal(1L, await ScalarLongAsync(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = ''
            """, ("tt", "S121_TT_SAME"), ("ok", OkVersion), ("ac", AgreementCode)));
        Assert.Equal(1L, await ScalarLongAsync(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = ''
              AND effective_from = @today AND effective_to IS NULL AND version = 2
            """, ("tt", "S121_TT_SAME"), ("ok", OkVersion), ("ac", AgreementCode), ("today", today)));
        // The UPDATED audit row landed (and no SUPERSEDED row — the branch routing pin).
        Assert.Equal(1L, await CountAuditRowsAsync("S121_TT_SAME", "UPDATED", versionBefore: 1, versionAfter: 2));
        Assert.Equal(0L, await CountAuditRowsAsync("S121_TT_SAME", "SUPERSEDED"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Branch 2 — cross-day supersede: SQL-backdated predecessor, PUT WITHOUT effectiveFrom.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The ruling #1 pin, cross-day branch: a predecessor whose
    /// <c>effective_from &lt; today</c> (seeded via SQL backdate — a fresh container has no
    /// aged rows) is CLOSED at today and a NEW open row INSERTs at version 1 — from a PUT
    /// body that OMITS <c>effectiveFrom</c>. Matcher-asserted 200, ETag "1" (the new row),
    /// close+insert proven row-wise, and the SUPERSEDED audit row LANDS.</summary>
    [Fact]
    public async Task Update_Put200_CrossDaySupersede_EffectiveFromOmitted_CloseInsertAndSupersededAuditLands()
    {
        using var admin = Admin();
        var etag = await CreateAsync(admin, "S121_TT_XDAY");
        Assert.Equal(1L, etag);

        // Age the predecessor: effective_from ← yesterday (drives the repo's cross-day branch).
        var today = Today();
        var yesterday = today.AddDays(-1);
        await ExecuteAsync(
            """
            UPDATE wage_type_mappings SET effective_from = @yesterday
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = ''
            """, ("yesterday", yesterday), ("tt", "S121_TT_XDAY"), ("ok", OkVersion), ("ac", AgreementCode));

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, "/api/admin/wage-type-mappings",
            PutJsonOmittingEffectiveFrom("S121_TT_XDAY"), ifMatchVersion: etag));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(200, (int)response.StatusCode);
        var truth = SpecRuntimeMatcher.ResolveSuccessContract(_spec, "/api/admin/wage-type-mappings", "put");
        SpecRuntimeMatcher.AssertSuccessMatches(_spec, truth, 200, body, "PUT /api/admin/wage-type-mappings (200, cross-day)");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, EntityKeys, "WTM PUT 200 (cross-day supersede)");
        Assert.Equal("SLS_S121B", root.GetProperty("wageType").GetString());
        Assert.Equal(1L, root.GetProperty("version").GetInt64());            // the NEW row's version
        Assert.Equal(1L, S118ContractAssert.EtagVersion(response));

        // Close + insert: the predecessor is CLOSED at today, the successor OPEN at today.
        Assert.Equal(2L, await ScalarLongAsync(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = ''
            """, ("tt", "S121_TT_XDAY"), ("ok", OkVersion), ("ac", AgreementCode)));
        Assert.Equal(1L, await ScalarLongAsync(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = ''
              AND effective_from = @yesterday AND effective_to = @today
            """, ("tt", "S121_TT_XDAY"), ("ok", OkVersion), ("ac", AgreementCode),
                 ("yesterday", yesterday), ("today", today)));
        Assert.Equal(1L, await ScalarLongAsync(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = ''
              AND effective_from = @today AND effective_to IS NULL AND version = 1
              AND wage_type = 'SLS_S121B'
            """, ("tt", "S121_TT_XDAY"), ("ok", OkVersion), ("ac", AgreementCode), ("today", today)));
        // The SUPERSEDED audit row landed (and no UPDATED row — the branch routing pin).
        Assert.Equal(1L, await CountAuditRowsAsync("S121_TT_XDAY", "SUPERSEDED", versionBefore: 1, versionAfter: 1));
        Assert.Equal(0L, await CountAuditRowsAsync("S121_TT_XDAY", "UPDATED"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The RETAINED validator — an explicit non-today value still 422s.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>Ruling #1 loosened OMISSION only: an EXPLICITLY-sent non-today
    /// <c>effectiveFrom</c> still hits the S29 same-day validator — 422 with the
    /// <c>suppliedEffectiveFrom</c>/<c>today</c> echo (the echo carries the SUPPLIED value,
    /// not the server default), and NOTHING mutates (no version bump, no audit row).</summary>
    [Fact]
    public async Task Update_Put422_ExplicitNonTodayEffectiveFrom_RetainedValidatorNothingMutates()
    {
        using var admin = Admin();
        var etag = await CreateAsync(admin, "S121_TT_VAL");

        var today = Today();
        var future = today.AddDays(30);
        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, "/api/admin/wage-type-mappings",
            PutJsonWithExplicitEffectiveFrom("S121_TT_VAL", future), ifMatchVersion: etag));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(422, (int)response.StatusCode);
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(future.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            root.GetProperty("suppliedEffectiveFrom").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            root.GetProperty("today").GetString());

        // Nothing mutated: still one open row at version 1, no update-flow audit rows.
        Assert.Equal(1L, await ScalarLongAsync(
            """
            SELECT COUNT(*) FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = ''
              AND effective_to IS NULL AND version = 1
            """, ("tt", "S121_TT_VAL"), ("ok", OkVersion), ("ac", AgreementCode)));
        Assert.Equal(0L, await CountAuditRowsAsync("S121_TT_VAL", "UPDATED"));
        Assert.Equal(0L, await CountAuditRowsAsync("S121_TT_VAL", "SUPERSEDED"));
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient Admin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.Date);

    /// <summary>Create a mapping through the REAL POST (effectiveFrom omitted ⇒ today);
    /// returns the 201 ETag version.</summary>
    private async Task<long> CreateAsync(HttpClient client, string timeType)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/wage-type-mappings", CreateJson(timeType)));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Wage-type-mapping create for {timeType} returned {(int)response.StatusCode}: {body}");
        return S118ContractAssert.EtagVersion(response);
    }

    private Task<long> CountAuditRowsAsync(
        string timeType, string action, long? versionBefore = null, long? versionAfter = null)
    {
        var sql =
            """
            SELECT COUNT(*) FROM wage_type_mapping_audit
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac AND position = ''
              AND action = @action
            """;
        var args = new List<(string, object)>
        {
            ("tt", timeType), ("ok", OkVersion), ("ac", AgreementCode), ("action", action),
        };
        if (versionBefore is { } vb)
        {
            sql += " AND version_before = @vb";
            args.Add(("vb", vb));
        }
        if (versionAfter is { } va)
        {
            sql += " AND version_after = @va";
            args.Add(("va", va));
        }
        return ScalarLongAsync(sql, args.ToArray());
    }

    private async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─────────────────────────────── request bodies (invariant JSON) ───────────────────────────────

    /// <summary>POST body — position omitted (⇒ "" on the row), effectiveFrom omitted (⇒ today).</summary>
    private static string CreateJson(string timeType)
        => $$"""
           { "timeType": "{{timeType}}", "wageType": "SLS_S121", "okVersion": "{{OkVersion}}",
             "agreementCode": "{{AgreementCode}}", "description": "S121 lønartsmapping" }
           """;

    /// <summary>PUT body — <c>effectiveFrom</c> OMITTED (ruling #1: the server owns today).
    /// This is the exact shape the graduated FE hook sends post-S121.</summary>
    private static string PutJsonOmittingEffectiveFrom(string timeType)
        => $$"""
           { "timeType": "{{timeType}}", "wageType": "SLS_S121B", "okVersion": "{{OkVersion}}",
             "agreementCode": "{{AgreementCode}}", "description": "S121 lønartsmapping (redigeret)" }
           """;

    /// <summary>PUT body — an EXPLICIT <c>effectiveFrom</c> for the retained-validator pin.</summary>
    private static string PutJsonWithExplicitEffectiveFrom(string timeType, DateOnly effectiveFrom)
        => $$"""
           { "timeType": "{{timeType}}", "wageType": "SLS_S121C", "okVersion": "{{OkVersion}}",
             "agreementCode": "{{AgreementCode}}", "description": "S121 valideringspin",
             "effectiveFrom": "{{effectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}}" }
           """;
}
