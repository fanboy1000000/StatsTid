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
/// S119 / TASK-11902 — the per-route spec≡runtime gate for the org-CONFIG read/visibility
/// surface drained in retrofit Pass 6 (TASK-11900): constraints GET (bare array, the
/// 13-member <c>ConfigConstraintResponse</c>), effective-config GET /{orgId} (the 14-member
/// <c>EffectiveConfigResponse</c> SIBLING — the two hand-maintained inline copies became TWO
/// separate records, shapes deliberately NOT merged), absence-types GET (the 2-member rows
/// off the hard-coded C# dict — and the binder-REQUIRED-but-dead <c>agreementCode</c>/
/// <c>okVersion</c> query params, which the spec documents as required so these tests SEND
/// them, plus the 400 pin when omitted), and visibility POST (the 3-member echo).
///
/// <para><b>The two-sibling pin (the fact-sheet drift-risk closure):</b> constraints rows and
/// the effective-config object share 13 fields but are DISTINCT wire shapes (bare-array rows
/// with no <c>orgId</c> vs an object root WITH <c>orgId</c>). Each is exact-key-set-pinned
/// independently, and the sibling relation itself is asserted (effective = constraints + the
/// <c>orgId</c> echo) — a future "merge the copies" wire change goes RED in both directions.</para>
///
/// <para><b>Per-op policy pins (the S119 P7 map):</b> the three reads are
/// <c>EmployeeOrAbove</c> — driven here by a plain EMPLOYEE client (positive floor pin);
/// visibility POST is <c>LocalAdminOrAbove</c> — the admin client succeeds and the SAME
/// employee client is pinned 403.</para>
///
/// <para><b>Seed discipline (FAIL-002):</b> a FRESH testcontainer per test (the established
/// Contracts-suite harness — never the compose stack on :5432). Orgs <c>S119CFG1</c>/
/// <c>S119CFG2</c> are S119-fresh SQL INPUT rows (the S117/S118 org-seed precedent); actors
/// <c>s119c_gadmin</c>/<c>s119c_emp</c> are JWT-only. The constraints walk rides the
/// boot/init AC-HK-PROSA ACTIVE central configs READ-ONLY. The visibility write lands only in
/// <c>absence_type_visibility</c> under the S119 org. Matcher + Support + S118ContractAssert
/// consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S119ConfigSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string AdminActorId = "s119c_gadmin";
    private const string EmployeeActorId = "s119c_emp";
    private const string EffectiveOrg = "S119CFG1";
    private const string AbsenceOrg = "S119CFG2";

    /// <summary>The EXACT 13 camelCase members of <c>ConfigConstraintResponse</c> (bare-array rows).</summary>
    private static readonly string[] ConstraintKeys =
    {
        "agreementCode", "okVersion", "weeklyNormHours", "maxFlexBalance", "flexCarryoverMax",
        "hasOvertime", "hasMerarbejde",
        "eveningSupplementEnabled", "nightSupplementEnabled", "weekendSupplementEnabled", "holidaySupplementEnabled",
        "onCallDutyEnabled", "onCallDutyRate",
    };

    /// <summary>The EXACT 14 members of the <c>EffectiveConfigResponse</c> SIBLING —
    /// <c>orgId</c> + the 13 shared fields (shapes NOT merged; the sibling-record rule).</summary>
    private static readonly string[] EffectiveKeys =
        new[] { "orgId" }.Concat(ConstraintKeys).ToArray();

    /// <summary>The EXACT 2 members of <c>AbsenceTypeResponse</c>.</summary>
    private static readonly string[] AbsenceTypeKeys = { "type", "label" };

    /// <summary>The EXACT 3 members of <c>AbsenceTypeVisibilityResponse</c>.</summary>
    private static readonly string[] VisibilityKeys = { "orgId", "absenceType", "isHidden" };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (baseline ACTIVE central configs)
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
    //  Op 1 — GET /api/config/constraints (bare array; the 13-member sibling).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The DB-backed central constraint reference (ADR-014): the matcher walks EVERY
    /// row of the bare array against the declared 13-member record; the boot/init ACTIVE
    /// AC/OK24 row is byte-asserted (exact key set — no <c>orgId</c> on this sibling) with
    /// decimal fidelity off the seed values. Driven by the EMPLOYEE client — the
    /// EmployeeOrAbove read floor pinned positively.</summary>
    [Fact]
    public async Task Constraints_Get200_BareArray_ThirteenMemberRowsExact()
    {
        using var employee = EmployeeClient(EffectiveOrg);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, "/api/config/constraints"),
            "/api/config/constraints", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        Assert.True(rows.GetArrayLength() >= 3); // at least the AC/HK/PROSA ACTIVE seeds

        var row = FindByAgreement(rows, "AC", "OK24");
        S118ContractAssert.AssertExactKeySet(row, ConstraintKeys, "constraints row (13-member sibling)");
        Assert.Equal(37.0m, row.GetProperty("weeklyNormHours").GetDecimal());
        Assert.Equal(150.0m, row.GetProperty("maxFlexBalance").GetDecimal());
        Assert.False(row.GetProperty("hasOvertime").GetBoolean());
        Assert.True(row.GetProperty("hasMerarbejde").GetBoolean());
        Assert.Equal(0.33m, row.GetProperty("onCallDutyRate").GetDecimal());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 2 — GET /api/config/{orgId} (the 14-member sibling; orgId echo).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The merged effective-config object: matcher + the EXACT 14-member key set,
    /// the <c>orgId</c> echo, and THE SIBLING RELATION itself — the effective key set is
    /// PROVABLY constraints-keys + <c>orgId</c> and nothing else, so a future merge of the two
    /// hand-maintained shapes (the drift risk the two records closed) is RED here AND in
    /// <see cref="Constraints_Get200_BareArray_ThirteenMemberRowsExact"/>. Values resolve from
    /// the central ACTIVE AC/OK24 config (no local profile exists for the S119 org).
    /// EMPLOYEE-driven (EmployeeOrAbove read floor, org-scope covered).</summary>
    [Fact]
    public async Task EffectiveConfig_Get200_FourteenMemberSibling_OrgIdEcho()
    {
        using var employee = EmployeeClient(EffectiveOrg);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get, $"/api/config/{EffectiveOrg}"),
            "/api/config/{orgId}", "get");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, EffectiveKeys, "effective-config 200 (14-member sibling)");
        Assert.Equal(EffectiveOrg, root.GetProperty("orgId").GetString()); // THE echo pin
        Assert.Equal("AC", root.GetProperty("agreementCode").GetString());
        Assert.Equal("OK24", root.GetProperty("okVersion").GetString());
        Assert.Equal(37.0m, root.GetProperty("weeklyNormHours").GetDecimal()); // central value, no local override

        // The sibling relation pinned structurally: effective = constraints + orgId, exactly.
        Assert.Equal(ConstraintKeys.Length + 1, EffectiveKeys.Length);
        Assert.Equal(ConstraintKeys, EffectiveKeys.Skip(1).ToArray());
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 3 — GET /api/config/{orgId}/absence-types (binder-REQUIRED query params SENT).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The 2-member rows off the hard-coded <c>AbsenceTypeLabels</c> dict. The
    /// binder-required-but-dead <c>agreementCode</c>/<c>okVersion</c> query params are SENT
    /// (the spec documents them required non-nullable — the Docker test honors the documented
    /// contract). With no visibility overrides all 10 dict entries serve; SICK_DAY/Sygedag is
    /// value-pinned. EMPLOYEE-driven.</summary>
    [Fact]
    public async Task AbsenceTypes_Get200_BinderRequiredQueryParamsSent_TwoMemberRows()
    {
        using var employee = EmployeeClient(AbsenceOrg);
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, employee,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get,
                $"/api/config/{AbsenceOrg}/absence-types?agreementCode=AC&okVersion=OK24"),
            "/api/config/{orgId}/absence-types", "get");

        var rows = JsonDocument.Parse(body).RootElement;
        Assert.Equal(10, rows.GetArrayLength()); // the full C# dict — no overrides on the fresh S119 org
        foreach (var row in rows.EnumerateArray())
            S118ContractAssert.AssertExactKeySet(row, AbsenceTypeKeys, "absence-type row");
        var sick = FindByType(rows, "SICK_DAY");
        Assert.Equal("Sygedag", sick.GetProperty("label").GetString());
    }

    /// <summary>The binder-REQUIRED pin's negative half: omitting the required-non-nullable
    /// query params is a 400 at the binder (the params are dead in the handler body, but the
    /// spec documents them required — the wire truth these tests gate).</summary>
    [Fact]
    public async Task AbsenceTypes_Get400_WhenBinderRequiredQueryParamsOmitted()
    {
        using var employee = EmployeeClient(AbsenceOrg);
        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Get, $"/api/config/{AbsenceOrg}/absence-types"));
        Assert.Equal(400, (int)response.StatusCode); // binder rejection — required params absent
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Op 4 — POST /api/config/{orgId}/absence-types/visibility (200; 3-member echo).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The LocalAdmin visibility toggle: matcher + the EXACT 3-member echo
    /// (<c>orgId</c>/<c>absenceType</c>/<c>isHidden</c>), then the NON-VACUOUS flip — the
    /// hidden type disappears from the subsequent absence-types GET (9 of 10 rows), proving
    /// the write landed rather than a constant echo.</summary>
    [Fact]
    public async Task Visibility_Post200_ThreeMemberEcho_AndTheAbsenceTypesFilterFlips()
    {
        using var admin = AdminClient();
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post,
                $"/api/config/{AbsenceOrg}/absence-types/visibility",
                """{ "absenceType": "LEAVE_WITHOUT_PAY", "isHidden": true }"""),
            "/api/config/{orgId}/absence-types/visibility", "post");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, VisibilityKeys, "visibility POST 200");
        Assert.Equal(AbsenceOrg, root.GetProperty("orgId").GetString());
        Assert.Equal("LEAVE_WITHOUT_PAY", root.GetProperty("absenceType").GetString());
        Assert.True(root.GetProperty("isHidden").GetBoolean());

        // The flip: the hidden type is filtered from the read (9 of the 10 dict entries).
        var after = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, admin,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Get,
                $"/api/config/{AbsenceOrg}/absence-types?agreementCode=AC&okVersion=OK24"),
            "/api/config/{orgId}/absence-types", "get");
        var rows = JsonDocument.Parse(after).RootElement;
        Assert.Equal(9, rows.GetArrayLength());
        Assert.DoesNotContain(rows.EnumerateArray(),
            r => string.Equals(r.GetProperty("type").GetString(), "LEAVE_WITHOUT_PAY", StringComparison.Ordinal));
    }

    /// <summary>The P7 per-op policy pin: visibility POST is <c>LocalAdminOrAbove</c> — the
    /// SAME employee identity that succeeds on every read above is 403 here (the write floor,
    /// pinned per-op rather than generalized).</summary>
    [Fact]
    public async Task Visibility_Post403_ForTheEmployeeActor_PolicyFloorPin()
    {
        using var employee = EmployeeClient(AbsenceOrg);
        using var response = await employee.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/config/{AbsenceOrg}/absence-types/visibility",
            """{ "absenceType": "LEAVE_WITHOUT_PAY", "isHidden": true }"""));
        Assert.Equal(403, (int)response.StatusCode);
    }

    // ─────────────────────────────── clients / seeds / helpers ───────────────────────────────

    private HttpClient AdminClient()
        => SpecRuntimeTestSupport.CreateGlobalAdminClient(_factory, AdminActorId, EffectiveOrg);

    /// <summary>A plain EMPLOYEE client with an ORG_ONLY scope over <paramref name="orgId"/> —
    /// the positive EmployeeOrAbove floor actor AND the 403 actor for the LocalAdmin write.
    /// Mirrors the Support helper's JWT minting (Support consumed AS-IS; the S117 ActorClient
    /// precedent).</summary>
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

    /// <summary>The two S119 org INPUT rows (the S117/S118 SQL org-seed precedent — orgs are
    /// input data; every config-family WRITE in this class goes through the real endpoints).
    /// AC/OK24 so the effective-config resolution rides the boot/init ACTIVE central config.</summary>
    private async Task SeedOrgsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                       materialized_path, agreement_code, ok_version) VALUES
                ('S119CFG1', 'S119 Config Styrelse 1', 'ORGANISATION', NULL, '/S119CFG1/', 'AC', 'OK24'),
                ('S119CFG2', 'S119 Config Styrelse 2', 'ORGANISATION', NULL, '/S119CFG2/', 'AC', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static JsonElement FindByAgreement(JsonElement array, string agreementCode, string okVersion)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("agreementCode").GetString(), agreementCode, StringComparison.Ordinal)
                && string.Equals(el.GetProperty("okVersion").GetString(), okVersion, StringComparison.Ordinal))
                return el;
        throw new XunitException($"Expected a constraints row for ({agreementCode}, {okVersion}) in: {array.GetRawText()}");
    }

    private static JsonElement FindByType(JsonElement array, string type)
    {
        foreach (var el in array.EnumerateArray())
            if (string.Equals(el.GetProperty("type").GetString(), type, StringComparison.Ordinal))
                return el;
        throw new XunitException($"Expected an absence-type row for '{type}' in: {array.GetRawText()}");
    }
}
