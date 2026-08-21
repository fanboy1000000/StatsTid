using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S132 / TASK-132-3c-4 (SEC-004, ruling 5b) — the belt-and-braces regression lock for the RETIRED
/// "sibling sub-org secondary-principal binding" hole.
///
/// <para>
/// <b>Plain-language (for a PM).</b> SEC-004 was the worry that an HR user scoped to one part of a
/// ministry could name a stand-in approver ("vikar") for a manager in a NEIGHBOURING part of the
/// same ministry — reaching across an org boundary they should not cross. That whole worry rested on
/// the OLD org model, where authority was INHERITED down a MINISTRY → STYRELSE → AFDELING → TEAM
/// tree, so anything "under the same styrelse" was treated as in-reach. The S92–S95 flat-authority
/// reform (ADR-035 / ADR-038) DELETED that inheritance: authority is now decided by <b>exact
/// Organisation equality</b> — two users are "same Organisation" only when their <c>primary_org_id</c>
/// values are literally equal, with no walk up a parent tree. That change makes the sibling-sub-org
/// binding STRUCTURALLY IMPOSSIBLE, so SEC-004 is CLOSED-BY-REFORM (not closed by a targeted fix).
/// This test PINS that guarantee so the hole cannot silently reopen.
/// </para>
///
/// <para>
/// <b>Why this is a CONFIRMING test (green on old AND new — exempt from the RED-on-old rule).</b> The
/// premise (nested sub-org/styrelse model + <c>ValidateSameTreeAsync</c> + <c>ORG_AND_DESCENDANTS</c>)
/// is ALREADY retired, so there is no "old code" to fail against — the exact-equality behaviour these
/// tests assert has been the live behaviour since S95. This is a regression LOCK: it is green today
/// and must stay green. (Per the S132 task charter, confirming tests of already-retired premises are
/// explicitly exempt from RED-on-old.)
/// </para>
///
/// <para>
/// <b>The sibling fixture — what makes this DISCRIMINATING (and distinct from the existing
/// <c>AdminVikarOnBehalfTests.AdminPost_CrossTreeVikar…</c>).</b> The seed orgs (init.sql) place
/// STY01 (<c>/MIN01/STY01/</c>) and STY02 (<c>/MIN01/STY02/</c>) as SIBLING Organisations under the
/// SAME parent MAO MIN01 — i.e. exactly the pair the OLD nested model would have treated as "the same
/// styrelse subtree" and therefore bindable. (The existing cross-tree test uses STY02 vs STY05, which
/// sit under DIFFERENT parents MIN01/MIN02 — that pins the cross-ministry case, NOT the
/// sibling-under-one-ministry case SEC-004 named.) The stand-in candidate <see cref="VikSibling"/>
/// lives in STY01 but is given an extra covering LOCAL_LEADER scope on STY02, so it PASSES the vikar
/// coverage census — which means the ONLY thing that can reject the binding is the exact-Organisation
/// guard (<see cref="ReportingLineRepository.ValidateSameOrganisationAsync"/>, STY01 ≠ STY02 →
/// <see cref="CrossOrganisationAssignmentException"/> → 400). That is what proves the guarantee is
/// "exact-Organisation equality," not "shared-parent/subtree membership."
/// </para>
///
/// <para>
/// Endpoint-level via <see cref="StatsTidWebApplicationFactory"/> (the real Backend.Api over a fresh
/// testcontainer), driving the actual secondary-principal binding surface
/// <c>POST /api/admin/reporting-lines/{managerId}/vikar</c> — the same authorization path SEC-004
/// concerned. Direct DB reads for the manager_vikar assertions and the fixture preconditions.
/// Reference: ADR-035 (flat authority) D3; ADR-038 D5; docs/SECURITY.md secondary-principal binding.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S132Sec004SiblingOrgVikarBindingTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // Sibling Organisations under the SAME parent MAO MIN01 (init.sql seed):
    //   /MIN01/STY01/  and  /MIN01/STY02/  — what the OLD nested model called "the same styrelse".
    private const string OrgSty01 = "STY01";
    private const string OrgSty02 = "STY02";
    private const string ParentMao = "MIN01";

    // Test users (own ids, prefixed sec004_ so they never collide with the init.sql demo seed).
    private const string HrActor = "sec004_hr";          // LOCAL_HR scoped over BOTH STY01 + STY02 (the "HR scoped across the styrelse" premise)
    private const string Mgr = "sec004_mgr";             // STY02 — the absent manager (LOCAL_LEADER)
    private const string Emp = "sec004_emp";             // STY02 — reports PRIMARY to Mgr (the report the vikar must cover)
    private const string VikSibling = "sec004_vik_sib";  // STY01 (sibling org) + a COVERING STY02 leader scope — the NEGATIVE subject
    private const string VikSame = "sec004_vik_same";    // STY02 (same org as Mgr) — the POSITIVE-CONTROL subject

    private static readonly string[] AllUsers = { HrActor, Mgr, Emp, VikSibling, VikSame };

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;
    private ReportingLineRepository _rlRepo = null!;
    private ManagerVikarRepository _vikarRepo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        _vikarRepo = new ManagerVikarRepository(_dbFactory);
        _rlRepo = new ReportingLineRepository(_dbFactory, _vikarRepo);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await CleanupAsync(conn);
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await CleanupAsync(conn);
        }
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The regression lock — negative (sibling org rejected) + positive control (same org allowed)
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// NEGATIVE — an actor CANNOT bind a stand-in whose Organisation is a DIFFERENT Organisation, even
    /// one that (under the retired nested model) would have been a SIBLING sub-org of the same
    /// styrelse. STY01 and STY02 share the parent MAO MIN01, yet the binding is REJECTED (400) by the
    /// exact-Organisation guard — proving equality of <c>primary_org_id</c>, not subtree membership,
    /// governs. Discriminating: the sibling vikar first PASSES the coverage census (it holds a covering
    /// STY02 leader scope), so the same-Organisation guard is provably the sole cause of the rejection.
    /// </summary>
    [Fact]
    public async Task Bind_SiblingSubOrgVikar_SameParentMao_Rejected_ExactOrganisationEquality()
    {
        // ── Preconditions that make this the SEC-004 sibling scenario (not the cross-ministry one) ──
        // (1) STY01 and STY02 are DISTINCT Organisations that share the SAME parent MAO (MIN01) — the
        //     "sibling sub-org of the same styrelse" the old inherited model would have permitted.
        Assert.Equal(ParentMao, await ReadParentOrgAsync(OrgSty01));
        Assert.Equal(ParentMao, await ReadParentOrgAsync(OrgSty02));
        Assert.NotEqual(OrgSty01, OrgSty02);
        // (2) The sibling vikar WOULD pass the coverage census (it holds a covering STY02 leader
        //     scope) — so the rejection below can ONLY be the exact-Organisation guard, never coverage.
        Assert.True(await CountAsync(
            "SELECT COUNT(*) FROM role_assignments WHERE user_id = @id AND org_id = @org AND role_id = 'LOCAL_LEADER'",
            ("id", VikSibling), ("org", OrgSty02)) >= 1);

        var client = HrClient(HrActor, primaryOrg: OrgSty01, scopeOrgs: new[] { OrgSty01, OrgSty02 });
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = VikSibling, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd") });

        // Rejected by exact-Organisation equality (STY01 ≠ STY02): 400 with the same-Organisation
        // message (the server still phrases it "styrelse (tree)"), NOT a coverage/scope message.
        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
        var err = await rsp.Content.ReadFromJsonAsync<ErrorBody>();
        Assert.NotNull(err);
        Assert.Contains("styrelse", err!.error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(err.uncoveredEmployeeIds); // not the coverage-census body

        // No vikar was committed — the guard rejected before the INSERT.
        Assert.Null(await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr));
    }

    /// <summary>
    /// POSITIVE CONTROL — a SAME-Organisation binding is ALLOWED (200), so the negative above is a
    /// real cross-Organisation rejection and not a false-reject from some unrelated gate (a green
    /// negative alone could pass vacuously). The stand-in <see cref="VikSame"/> shares the manager's
    /// Organisation STY02, so exact-Organisation equality holds and the binding succeeds.
    /// </summary>
    [Fact]
    public async Task Bind_SameOrganisationVikar_Allowed()
    {
        var client = HrClient(HrActor, primaryOrg: OrgSty01, scopeOrgs: new[] { OrgSty01, OrgSty02 });
        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{Mgr}/vikar",
            new { vikarUserId = VikSame, effectiveTo = Today().AddDays(30).ToString("yyyy-MM-dd"), reason = "FERIE" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The manager_vikar row exists (active), stamped with the common Organisation (STY02).
        var row = await _vikarRepo.GetActiveByApproverAnyDateAsync(Mgr);
        Assert.NotNull(row);
        Assert.Equal(VikSame, row!.VikarUserId);
        Assert.Equal(OrgSty02, row.OrganisationId);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seed / cleanup
    // ════════════════════════════════════════════════════════════════════════════════

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@hr,      @hr,      '$2a$11$fake', 'SEC004 HR',       'sec004_hr@test.dk',      'STY01', 'AC', 'OK24', TRUE),
                (@mgr,     @mgr,     '$2a$11$fake', 'SEC004 Mgr',      'sec004_mgr@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@emp,     @emp,     '$2a$11$fake', 'SEC004 Emp',      'sec004_emp@test.dk',     'STY02', 'HK', 'OK24', TRUE),
                (@viksib,  @viksib,  '$2a$11$fake', 'SEC004 VikSib',   'sec004_vik_sib@test.dk', 'STY01', 'AC', 'OK24', TRUE),
                (@viksame, @viksame, '$2a$11$fake', 'SEC004 VikSame',  'sec004_vik_same@test.dk','STY02', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES
                -- HR actor scoped over BOTH sibling Organisations (the "HR spanning the old styrelse"
                -- premise). The STY02 grant satisfies the endpoint's LocalHR-floored actor gate.
                (@hr,      'LOCAL_HR',     'STY01', 'ORG_ONLY', 'TEST'),
                (@hr,      'LOCAL_HR',     'STY02', 'ORG_ONLY', 'TEST'),
                (@mgr,     'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                (@emp,     'EMPLOYEE',     'STY02', 'ORG_ONLY', 'TEST'),
                -- Sibling vikar: home STY01, PLUS a covering STY02 leader grant so it PASSES the
                -- coverage census — making the same-Organisation guard the sole rejection cause.
                (@viksib,  'LOCAL_LEADER', 'STY01', 'ORG_ONLY', 'TEST'),
                (@viksib,  'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST'),
                -- Same-Organisation vikar (positive control): STY02 leader (covers Emp).
                (@viksame, 'LOCAL_LEADER', 'STY02', 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            AddUserParams(cmd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Emp (STY02) reports PRIMARY to Mgr (STY02) — the manager's report the vikar must cover.
        await _rlRepo.AssignAsync(null, MakeLine(Emp, Mgr));
    }

    private void AddUserParams(NpgsqlCommand cmd)
    {
        cmd.Parameters.AddWithValue("hr", HrActor);
        cmd.Parameters.AddWithValue("mgr", Mgr);
        cmd.Parameters.AddWithValue("emp", Emp);
        cmd.Parameters.AddWithValue("viksib", VikSibling);
        cmd.Parameters.AddWithValue("viksame", VikSame);
    }

    private static ReportingLineModel MakeLine(string employeeId, string managerId) => new()
    {
        ReportingLineId = Guid.Empty,
        EmployeeId = employeeId,
        ManagerId = managerId,
        OrganisationId = OrgSty02,
        Relationship = "PRIMARY",
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Source = "MANUAL",
        Version = 0,
        CreatedBy = "TEST",
    };

    private async Task CleanupAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn,
            "DELETE FROM manager_vikar WHERE absent_approver_id = ANY(@ids) OR vikar_user_id = ANY(@ids)");
        await ExecAsync(conn,
            "DELETE FROM reporting_line_audit WHERE reporting_line_id IN (SELECT reporting_line_id FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids))");
        await ExecAsync(conn, "DELETE FROM reporting_lines WHERE employee_id = ANY(@ids) OR manager_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM role_assignments WHERE user_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM employee_profiles WHERE employee_id = ANY(@ids)");
        await ExecAsync(conn, "DELETE FROM user_agreement_codes WHERE user_id = ANY(@ids)");
        await ExecStreamsAsync(conn);
        await ExecAsync(conn, "DELETE FROM users WHERE user_id = ANY(@ids)");

        async Task ExecAsync(NpgsqlConnection c, string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c);
            cmd.Parameters.AddWithValue("ids", AllUsers);
            await cmd.ExecuteNonQueryAsync();
        }

        async Task ExecStreamsAsync(NpgsqlConnection c)
        {
            await using var cmd = new NpgsqlCommand("DELETE FROM outbox_events WHERE stream_id = ANY(@streams)", c);
            cmd.Parameters.AddWithValue("streams", AllUsers.Select(id => $"reporting-line-{id}").ToArray());
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════════════════════

    private sealed record ErrorBody(string error, string[]? uncoveredEmployeeIds, int? uncoveredCount);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Mints a LOCAL_HR bearer with one ORG_ONLY scope per <paramref name="scopeOrgs"/> entry
    /// (flat role-scope: exact Organisation membership, no subtree). The token's primary org (the audit
    /// discriminator) is decoupled from the access-granting scopes, mirroring
    /// <see cref="StatsTid.Tests.Regression.ReportingLine.AdminVikarOnBehalfTests"/>.</summary>
    private HttpClient HrClient(string userId, string primaryOrg, string[] scopeOrgs)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var scopes = scopeOrgs
            .Select(org => new RoleScope(StatsTidRoles.LocalHR, org, "ORG_ONLY"))
            .ToArray();
        var bearer = tokenService.GenerateToken(
            employeeId: userId, name: userId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: primaryOrg, scopes: scopes);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private async Task<string?> ReadParentOrgAsync(string orgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT parent_org_id FROM organizations WHERE org_id = @id", conn);
        cmd.Parameters.AddWithValue("id", orgId);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    private async Task<long> CountAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
