using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S148 / TASK-14806-T (HRP-013; R-19; ADR-027 D3, D5, D9) — "covered" on HR's two missing-approver lists
/// means the approval walk names someone for the day. Each fact reads BOTH surfaces through their real routes
/// on the same day and asserts they agree:
/// <list type="bullet">
/// <item>the per-organisation roster card, <c>GET /api/admin/reporting-lines/tree/{organisationId}/medarbejdere</c>
/// (each row's <c>isRoot</c> / <c>isOrphan</c>);</item>
/// <item>the cross-organisation roll-up, <c>GET /api/hr/follow-up/uncovered-approvers</c> (<c>orphans</c>).</item>
/// </list>
///
/// <para><b>Plain words.</b> A manager's last day is D. From D+1 the walk skips them (ADR-040, 5-day grace keeps
/// them switched on but not in charge), so a report with nobody above the manager has no approver and must show as
/// an orphan on both lists. A stand-in the manager appointed before leaving keeps the report covered (owner ruling
/// Q-B = (c)); so does an ACTING line above.</para>
///
/// <para><b>Dates</b> (weekdays checked). D = Wednesday 2026-11-04 (the manager's last day employed), D+1 = Thursday
/// 2026-11-05, D+5 = Monday 2026-11-09 (the last grace day). The stand-in is appointed before D (created 2026-10-28)
/// and runs to Friday 2026-11-20. No absences are seeded. One host per checked day, ALL booted before any row is
/// seeded (PAT-008); the leaver job is off so the grace state seeded directly cannot be flipped mid-fact.</para>
///
/// <para>Every fact owns its container, organisation and users. Docker-gated: CI-pending until CI-W1.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OrphanCoverageTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY_S148_OC";

    /// <summary>The departing manager's last day employed — a Wednesday.</summary>
    private static readonly DateOnly D = new(2026, 11, 4);
    private static readonly DateOnly D1 = D.AddDays(1);
    private static readonly DateOnly D5 = D.AddDays(5);
    private static readonly DateOnly StandInUntil = new(2026, 11, 20);
    private static readonly DateTime StandInAppointed = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private DbConnectionFactory _dbFactory = null!;
    private readonly List<WebApplicationFactory<Program>> _hosts = new();

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString, withoutLeaverJob: true);
        _dbFactory = new DbConnectionFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        foreach (var h in _hosts)
            h.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  The four facts
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Kind F (AC-21a-a).</b> M (LocalLeader, no line of their own, last day D) has one report R. On D the report
    /// is covered (M is still acting); on D+1 and on D+5 the walk names nobody, so R is an orphan on the card AND on
    /// the roll-up, and on neither is R a root (nobody reports to R).
    /// <para><b>Red conditions:</b> <c>Assert.Equal("Orphan", ...)</c> for R on D+1 (and D+5). On the base
    /// (<c>a03d61ae</c>) both reads count R's open PRIMARY line to M as cover whatever M's state
    /// (<c>ApprovalPeriodRepository</c> roster SQL, <c>HrFollowUpApprovalReadRepository</c> NOT EXISTS pair), and
    /// M is still <c>is_active</c> in the grace — so R is on neither list and the assertion fails on both surfaces.
    /// The D control keeps the fixture honest: R is covered there, so the D+1 orphan is caused by the departure
    /// and nothing else.</para>
    /// </summary>
    [Fact]
    public async Task ManagerDeparts_NoManagerAbove_ReportIsOrphan_CardEqualsRollUp()
    {
        var onD = Boot(D);
        var onD1 = Boot(D1);
        var onD5 = Boot(D5);
        var users = new[] { "oc_a_m", "oc_a_r" };
        await SeedAsync(async conn =>
        {
            await UserAsync(conn, "oc_a_m", "LOCAL_LEADER", endDate: D);
            await UserAsync(conn, "oc_a_r", "EMPLOYEE");
            await LineAsync("oc_a_r", "oc_a_m");
        });

        var control = await ReadBothAsync(onD, users);
        Assert.Equal("Covered", control["oc_a_r"]); // M's last day: still acting.

        foreach (var host in new[] { onD1, onD5 })
        {
            var seen = await ReadBothAsync(host, users);
            Assert.Equal("Orphan", seen["oc_a_r"]);
        }
    }

    /// <summary>
    /// <b>Kind R — regression pin, holds today (AC-21a-b; ruled C4).</b> As AC-21a-a, plus a stand-in V that M
    /// appointed BEFORE D (created 2026-10-28, until 2026-11-20): R stays covered on D+1 and D+5, on both surfaces
    /// (owner ruling Q-B = (c); ADR-027 D14).
    /// <para><b>Discriminating assertion:</b> <c>Assert.Equal("Covered", ...)</c> for R. A predicate that dropped the
    /// stand-in (a walk without the vikar consult, or a "covered = M acting" test) finds nobody above the departed M
    /// and turns R into an orphan — this assertion fails. It holds on the base only because R's open line counts as
    /// cover there, which is why it is R and not F.</para>
    /// </summary>
    [Fact]
    public async Task StandInAppointedBeforeDeparture_Covered()
    {
        var onD1 = Boot(D1);
        var onD5 = Boot(D5);
        var users = new[] { "oc_b_m", "oc_b_v", "oc_b_r" };
        await SeedAsync(async conn =>
        {
            await UserAsync(conn, "oc_b_m", "LOCAL_LEADER", endDate: D);
            await UserAsync(conn, "oc_b_v", "LOCAL_LEADER");
            await UserAsync(conn, "oc_b_r", "EMPLOYEE");
            await LineAsync("oc_b_r", "oc_b_m");
            await StandInAsync(conn, absent: "oc_b_m", standIn: "oc_b_v");
        });

        foreach (var host in new[] { onD1, onD5 })
        {
            var seen = await ReadBothAsync(host, users);
            Assert.Equal("Covered", seen["oc_b_r"]);
        }
    }

    /// <summary>
    /// <b>Kind R — regression pin, holds today (AC-21a-c; ruled C4).</b> As AC-21a-a, plus an ACTING line ABOVE
    /// the departed manager: M → ACTING → A (active). Which line (BC-3): the ACTING line sits on M, so the walk for
    /// R reaches it after one climb past the departed M. R stays covered on D+1 and D+5, on both surfaces.
    /// <para><b>Discriminating assertion:</b> <c>Assert.Equal("Covered", ...)</c> for R. A predicate that ignored
    /// ACTING lines (a PRIMARY-only walk) finds nobody above M and makes R an orphan — this assertion fails. It
    /// holds on the base only because R's open PRIMARY line counts as cover there.</para>
    /// </summary>
    [Fact]
    public async Task ActingManagerAbove_Covered()
    {
        var onD1 = Boot(D1);
        var onD5 = Boot(D5);
        var users = new[] { "oc_c_m", "oc_c_a", "oc_c_r" };
        await SeedAsync(async conn =>
        {
            await UserAsync(conn, "oc_c_m", "LOCAL_LEADER", endDate: D);
            await UserAsync(conn, "oc_c_a", "LOCAL_LEADER");
            await UserAsync(conn, "oc_c_r", "EMPLOYEE");
            await LineAsync("oc_c_r", "oc_c_m");
            await LineAsync("oc_c_m", "oc_c_a", relationship: "ACTING");
        });

        foreach (var host in new[] { onD1, onD5 })
        {
            var seen = await ReadBothAsync(host, users);
            Assert.Equal("Covered", seen["oc_c_r"]);
        }
    }

    /// <summary>
    /// <b>Evidence (AC-21a-d; NR-1 ruled (a); R-19).</b> One organisation, four disjoint shapes, all read on D+1
    /// on both surfaces, each person pinned to the classification TASK-14806 chose, card = roll-up for everyone:
    /// <code>
    /// shape D  G (no line)  ← M (departs D) ← R          G Root,   M Covered, R Covered   (reports escalate past M)
    /// shape A  Ma (departs D, nobody above) ← Ra          Ma Root,  Ra Orphan
    /// shape B  Mb (departs D) ← Rb ; stand-in Vb of Mb    Mb Root,  Rb Covered, Vb Orphan
    /// shape E  Ae (active, away) ← Pe ; stand-in Ve of Ae Ae Root,  Pe Covered, Ve Orphan
    /// </code>
    /// <para><b>The wording chosen for Root (R-19), ADR-027 D9's literal root:</b> the walk names nobody, the person
    /// holds no open PRIMARY line of their own, and another member of the read reports to them on an open PRIMARY
    /// line. Every other uncovered person is an Orphan. (The shape where a middle manager keeps their own line to a
    /// departed top is pinned by <see cref="ManagerOfManagersLosesApprover_IsOrphan_CardEqualsRollUp"/>.)</para>
    /// <para><b>Does this fixture discriminate the readings?</b> Reading (ii), "the walk names them for someone",
    /// would classify Ma, Mb and Ae as Orphan and Vb and Ve as Root — so shapes A, B and E discriminate (i) from
    /// (ii). Shape D does not: every person is classified the same under all three. Reading (iii), "holds any open
    /// PRIMARY line", differs from (i) only for a line from a person OUTSIDE the read (a switched-off report whose line
    /// stays open); no such person is seeded, so this fixture does NOT discriminate (i) from (iii).</para>
    /// <para><b>Red conditions:</b> on the base, Ra is covered by her open line to the departed Ma, so
    /// <c>Assert.Equal("Orphan", ...)</c> for Ra fails; Rb likewise is covered on the base and only the stand-in keeps
    /// her covered now, so a predicate dropping the stand-in fails <c>Rb == Covered</c>; a reading (ii) predicate
    /// fails Ma, Mb, Ae (Root) and Vb, Ve (Orphan).</para>
    /// </summary>
    [Fact]
    public async Task DepartedManagerWithReportsEscalatingPast_ClassifiedOnBothSurfaces()
    {
        var onD1 = Boot(D1);
        var users = new[]
        {
            "oc_d_g", "oc_d_m", "oc_d_r",
            "oc_d_ma", "oc_d_ra",
            "oc_d_mb", "oc_d_rb", "oc_d_vb",
            "oc_d_ae", "oc_d_pe", "oc_d_ve",
        };
        await SeedAsync(async conn =>
        {
            // Shape D.
            await UserAsync(conn, "oc_d_g", "LOCAL_LEADER");
            await UserAsync(conn, "oc_d_m", "LOCAL_LEADER", endDate: D);
            await UserAsync(conn, "oc_d_r", "EMPLOYEE");
            await LineAsync("oc_d_m", "oc_d_g");
            await LineAsync("oc_d_r", "oc_d_m");
            // Shape A.
            await UserAsync(conn, "oc_d_ma", "LOCAL_LEADER", endDate: D);
            await UserAsync(conn, "oc_d_ra", "EMPLOYEE");
            await LineAsync("oc_d_ra", "oc_d_ma");
            // Shape B.
            await UserAsync(conn, "oc_d_mb", "LOCAL_LEADER", endDate: D);
            await UserAsync(conn, "oc_d_vb", "LOCAL_LEADER");
            await UserAsync(conn, "oc_d_rb", "EMPLOYEE");
            await LineAsync("oc_d_rb", "oc_d_mb");
            await StandInAsync(conn, absent: "oc_d_mb", standIn: "oc_d_vb");
            // Shape E.
            await UserAsync(conn, "oc_d_ae", "LOCAL_LEADER");
            await UserAsync(conn, "oc_d_ve", "LOCAL_LEADER");
            await UserAsync(conn, "oc_d_pe", "EMPLOYEE");
            await LineAsync("oc_d_pe", "oc_d_ae");
            await StandInAsync(conn, absent: "oc_d_ae", standIn: "oc_d_ve");
        });

        var seen = await ReadBothAsync(onD1, users);

        var expected = new Dictionary<string, string>
        {
            ["oc_d_g"] = "Root",
            ["oc_d_m"] = "Covered",
            ["oc_d_r"] = "Covered",
            ["oc_d_ma"] = "Root",
            ["oc_d_ra"] = "Orphan",
            ["oc_d_mb"] = "Root",
            ["oc_d_rb"] = "Covered",
            ["oc_d_vb"] = "Orphan",
            ["oc_d_ae"] = "Root",
            ["oc_d_pe"] = "Covered",
            ["oc_d_ve"] = "Orphan",
        };
        Assert.Equal(users.Length, expected.Count);
        foreach (var (id, kind) in expected)
            Assert.Equal(kind, seen[id]);
    }

    /// <summary>
    /// <b>Kind F (shape F; adjudication 2a, fix-up 10).</b> G has no line above and leaves on D; M reports to G; R
    /// reports to M. On D+1 and on D+5: G is a Root (walk names nobody, no line of their own, M reports to them), M is
    /// an Orphan (G is gone and nobody is above, so no approver exists for M's month, although R reports to M), and R
    /// is Covered (M is still acting). Card = roll-up for all three.
    /// <para><b>Red conditions:</b> <c>Assert.Equal("Orphan", seen["oc_f_m"])</c>. On the base (<c>a03d61ae</c>) M's
    /// open line to G counts as cover, so M is on neither list and reads as Covered; under the earlier reading (i)
    /// (<c>ae3968a0</c>) R reporting to M made M a Root. Either way the assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task ManagerOfManagersLosesApprover_IsOrphan_CardEqualsRollUp()
    {
        var onD1 = Boot(D1);
        var onD5 = Boot(D5);
        var users = new[] { "oc_f_g", "oc_f_m", "oc_f_r" };
        await SeedAsync(async conn =>
        {
            await UserAsync(conn, "oc_f_g", "LOCAL_LEADER", endDate: D);
            await UserAsync(conn, "oc_f_m", "LOCAL_LEADER");
            await UserAsync(conn, "oc_f_r", "EMPLOYEE");
            await LineAsync("oc_f_m", "oc_f_g");
            await LineAsync("oc_f_r", "oc_f_m");
        });

        foreach (var host in new[] { onD1, onD5 })
        {
            var seen = await ReadBothAsync(host, users);
            Assert.Equal("Root", seen["oc_f_g"]);
            Assert.Equal("Orphan", seen["oc_f_m"]);
            Assert.Equal("Covered", seen["oc_f_r"]);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Reading both surfaces
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the card and the roll-up on <paramref name="host"/>'s day for the given users, asserts the two surfaces
    /// agree for every one (the card's <c>isOrphan</c> equals membership of the roll-up's <c>orphans</c>; a row is
    /// never both root and orphan), and returns each user's classification: "Orphan", "Root" or "Covered". The
    /// roll-up does not serve roots, so root-ness is the card's alone.
    /// </summary>
    private static async Task<Dictionary<string, string>> ReadBothAsync(
        WebApplicationFactory<Program> host, IEnumerable<string> users)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", HrToken());

        var cardRsp = await client.GetAsync($"/api/admin/reporting-lines/tree/{Org}/medarbejdere");
        Assert.Equal(HttpStatusCode.OK, cardRsp.StatusCode);
        var card = (await cardRsp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("employees")
            .EnumerateArray().ToDictionary(e => e.GetProperty("employeeId").GetString()!);

        var rollUpRsp = await client.GetAsync("/api/hr/follow-up/uncovered-approvers");
        Assert.Equal(HttpStatusCode.OK, rollUpRsp.StatusCode);
        var rollUpOrphans = (await rollUpRsp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orphans")
            .EnumerateArray().Select(o => o.GetProperty("employeeId").GetString()!).ToHashSet(StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in users)
        {
            Assert.True(card.TryGetValue(id, out var row), $"{id} missing from the card");
            var isRoot = row.GetProperty("isRoot").GetBoolean();
            var isOrphan = row.GetProperty("isOrphan").GetBoolean();
            Assert.False(isRoot && isOrphan, $"{id} is both root and orphan on the card");
            Assert.Equal(isOrphan, rollUpOrphans.Contains(id)); // card = roll-up
            result[id] = isOrphan ? "Orphan" : isRoot ? "Root" : "Covered";
        }
        return result;
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Seeding
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A host whose clock is pinned to <paramref name="today"/>, booted NOW (PAT-008: the seeders run
    /// at boot, so every host a fact uses boots before the fact seeds anything).</summary>
    private WebApplicationFactory<Program> Boot(DateOnly today)
    {
        var host = _factory.WithFixedToday(today);
        _ = host.CreateClient();
        _hosts.Add(host);
        return host;
    }

    private async Task SeedAsync(Func<NpgsqlConnection, Task> seed)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await Exec(conn,
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version)
            VALUES (@org, 'S148 orphan coverage', 'ORGANISATION', NULL, @path, 'HK', 'OK24')
            ON CONFLICT (org_id) DO NOTHING
            """,
            ("org", Org), ("path", $"/{Org}/"));
        await seed(conn);
    }

    private static async Task UserAsync(NpgsqlConnection conn, string id, string roleId, DateOnly? endDate = null)
    {
        await Exec(conn,
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active, employment_end_date)
            VALUES (@id, @id, '$2a$11$fake', @id, @email, @org, 'HK', 'OK24', TRUE, @end)
            """,
            ("id", id), ("email", id + "@test.dk"), ("org", Org),
            ("end", endDate.HasValue ? endDate.Value : DBNull.Value));
        await Exec(conn,
            "INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by, expires_at) VALUES (@id, @role, @org, 'ORG_ONLY', 'TEST', NULL)",
            ("id", id), ("role", roleId), ("org", Org));
    }

    private async Task LineAsync(string employeeId, string managerId, string relationship = "PRIMARY")
    {
        await new ReportingLineRepository(_dbFactory).AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = employeeId,
            ManagerId = managerId,
            OrganisationId = Org,
            Relationship = relationship,
            EffectiveFrom = D.AddYears(-1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });
    }

    /// <summary>An active stand-in row appointed before D, in force through <see cref="StandInUntil"/>.</summary>
    private static Task StandInAsync(NpgsqlConnection conn, string absent, string standIn) => Exec(conn,
        """
        INSERT INTO manager_vikar (absent_approver_id, vikar_user_id, until_date, reason, organisation_id, version, created_by, created_at, effective_to)
        VALUES (@absent, @vikar, @until, 'ANDET', @org, 1, 'TEST', @created, NULL)
        """,
        ("absent", absent), ("vikar", standIn), ("until", StandInUntil), ("org", Org), ("created", StandInAppointed));

    private static async Task Exec(NpgsqlConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        // NpgsqlBatch (not NpgsqlCommand): every caller passes a literal and the values travel as parameters,
        // so CA2100's warning budget does not grow.
        await using var batch = new NpgsqlBatch(conn);
        var cmd = new NpgsqlBatchCommand(sql);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        batch.BatchCommands.Add(cmd);
        await batch.ExecuteNonQueryAsync();
    }

    private static string HrToken() => new JwtTokenService(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    }).GenerateToken(
        employeeId: "hr_s148_oc_actor", name: "hr_s148_oc_actor", role: StatsTidRoles.LocalHR, agreementCode: "HK",
        orgId: Org, scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, Org, "ORG_ONLY") });
}
