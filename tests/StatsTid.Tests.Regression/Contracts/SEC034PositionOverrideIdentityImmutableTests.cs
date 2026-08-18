using System.Net;
using System.Net.Http;
using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// SEC-034 — a position override's IDENTITY triple (agreement_code, ok_version, position_code) is
/// immutable. The repository's <c>UpdateAsync</c> writes only the VALUE columns, so the stored
/// row's identity never moves. BUT before this fix the PUT handler still stamped the audit row
/// (<c>new_data = Serialize(body)</c>), the <c>PositionOverrideUpdated</c> event, and its
/// projection from the BODY's triple — so a PUT whose body carried a DIFFERENT triple produced a
/// "phantom-identity" audit claiming the override moved when the DB row did not. That is SEC-034.
///
/// <para><b>Owner ruling — REJECT the identity change.</b> The handler now returns <b>409</b> for
/// any PUT whose body changes ANY of the three identity fields, BEFORE any audit/outbox/projection
/// emit — closing the phantom-audit path. A value-only PUT (norm/flex/description) is unaffected
/// and still returns 200.</para>
///
/// <para><b>RED-before-green:</b> each identity-change variant returned <b>200</b> (with a
/// phantom-identity audit) on the pre-fix handler; it now returns <b>409</b>. The If-Match version
/// supplied is the CORRECT current version (v1), so the 409 is the identity guard specifically —
/// not a 412 (stale version) or 428 (missing header).</para>
///
/// <para>Fresh testcontainer per test (Docker-gated). The GlobalAdmin client is minted by
/// <see cref="SpecRuntimeTestSupport"/>; each test drives its OWN row under a <c>SEC034_*</c>
/// agreement code on the test-seeded position <c>SEC034_POS</c> — the four init.sql seed overrides
/// are never touched.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SEC034PositionOverrideIdentityImmutableTests : IAsyncLifetime
{
    private const string ActorId = "sec034_gadmin";
    private const string JwtOrg = "SEC034"; // JWT claim only — override rows are GLOBAL (no org FK)
    private const string AgreementCode = "SEC034_PO";
    private const string OkVersion = "OKSEC034";
    private const string PositionCode = "SEC034_POS"; // test-seeded positions row (FK target)

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
            VALUES ('SEC034_POS', 'SEC-034 Testposition', 'AC')
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
    //  Core — a PUT that changes ANY identity field is rejected 409 BEFORE any emit.
    //  Pre-fix each of these was 200 + a phantom-identity audit row.
    // ════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("SEC034_XX", OkVersion, PositionCode)]        // changed agreementCode
    [InlineData(AgreementCode, "OKOTHER", PositionCode)]       // changed okVersion
    [InlineData(AgreementCode, OkVersion, "SEC034_OTHER")]     // changed positionCode
    public async Task Put_IdentityChange_Returns409_BeforeAnyEmit(
        string putAgreementCode, string putOkVersion, string putPositionCode)
    {
        using var admin = Admin();
        var (overrideId, v1) = await CreateAsync(admin);

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/admin/position-overrides/{overrideId}",
            Body(putAgreementCode, putOkVersion, putPositionCode, weeklyNormHours: "36.75"),
            ifMatchVersion: v1));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); // 409 — RED: was 200 pre-fix
        Assert.Contains("immutable", body, StringComparison.OrdinalIgnoreCase);

        // No phantom-identity audit: the ONLY audit row is the CREATE — no UPDATED row was emitted,
        // and the stored row's identity + version are untouched (still v1).
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM position_override_config_audit WHERE override_id = @id AND action = 'UPDATED'",
            overrideId));
        Assert.Equal(1L, await ScalarLongAsync(
            "SELECT version FROM position_override_configs WHERE override_id = @id", overrideId));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Unaffected — a value-only PUT (same identity) still returns 200, bumps version,
    //  and its audit row carries the CORRECT (unchanged) identity — never a phantom.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Put_ValueOnly_Returns200_AuditCarriesCorrectIdentity()
    {
        using var admin = Admin();
        var (overrideId, v1) = await CreateAsync(admin);

        using var response = await admin.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/admin/position-overrides/{overrideId}",
            Body(AgreementCode, OkVersion, PositionCode, weeklyNormHours: "36.75"),
            ifMatchVersion: v1));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // 200 — value-only path unaffected
        var root = JsonDocument.Parse(body).RootElement;
        Assert.Equal(2L, root.GetProperty("version").GetInt64());
        Assert.Equal(36.75m, root.GetProperty("weeklyNormHours").GetDecimal());
        // Response echoes the true identity (repo ignores identity on update).
        Assert.Equal(AgreementCode, root.GetProperty("agreementCode").GetString());
        Assert.Equal(OkVersion, root.GetProperty("okVersion").GetString());
        Assert.Equal(PositionCode, root.GetProperty("positionCode").GetString());

        // The UPDATED audit row's new_data carries the CORRECT (unchanged) identity triple —
        // the guard guarantees body identity == stored identity on the 200 path.
        var newData = await ScalarStringAsync(
            """
            SELECT new_data::text FROM position_override_config_audit
            WHERE override_id = @id AND action = 'UPDATED'
            ORDER BY audit_id DESC LIMIT 1
            """, overrideId);
        Assert.NotNull(newData);
        Assert.Contains(AgreementCode, newData);
        Assert.Contains(OkVersion, newData);
        Assert.Contains(PositionCode, newData);
    }

    // ─────────────────────────────── clients / helpers ───────────────────────────────

    private HttpClient Admin()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, ActorId, JwtOrg);

    private async Task<(Guid OverrideId, long EtagVersion)> CreateAsync(HttpClient client)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, "/api/admin/position-overrides",
            Body(AgreementCode, OkVersion, PositionCode, weeklyNormHours: "null")));
        var body = await response.Content.ReadAsStringAsync();
        if ((int)response.StatusCode != 201)
            throw new XunitException($"Position-override create returned {(int)response.StatusCode}: {body}");
        var overrideId = JsonDocument.Parse(body).RootElement.GetProperty("overrideId").GetGuid();
        return (overrideId, S118ContractAssert.EtagVersion(response));
    }

    private async Task ExecAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarLongAsync(string sql, Guid overrideId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", overrideId);
        return Convert.ToInt64((await cmd.ExecuteScalarAsync())!);
    }

    private async Task<string?> ScalarStringAsync(string sql, Guid overrideId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", overrideId);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    /// <summary>Create/update body — the same 8-member request shape serves both verbs.
    /// <paramref name="weeklyNormHours"/> is passed as raw JSON ("null" or a literal).</summary>
    private static string Body(string agreementCode, string okVersion, string positionCode, string weeklyNormHours)
        => $$"""
           { "agreementCode": "{{agreementCode}}", "okVersion": "{{okVersion}}",
             "positionCode": "{{positionCode}}",
             "maxFlexBalance": 120.5, "flexCarryoverMax": 40.25, "normPeriodWeeks": 4,
             "weeklyNormHours": {{weeklyNormHours}}, "description": "SEC-034 positionsundtagelse" }
           """;
}
