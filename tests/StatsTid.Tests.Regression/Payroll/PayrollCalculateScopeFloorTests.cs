using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S145 / Step 7a W-1 (WARNING) — the Payroll host's <c>POST /api/payroll/calculate-and-export</c> must apply
/// the S76 mixed-role scope FLOOR (<c>docs/SECURITY.md</c> section "The mixed-role scope-floor invariant
/// (S76 / B1)"). A new file rather than an append: no payroll host file pins the route's org-scope check, and
/// the harness pieces it needs (<see cref="PayrollHostFactory"/>, the dev signing key) are shared types, so a
/// small dedicated class is the established shape (see <c>PayrollHostWeeklyTests</c>).
///
/// <para><b>Plain language.</b> The route is open to LocalAdmin and above, and it also checks that the caller's
/// scope covers the target employee's organisation. Before the fix that check ignored WHICH ROLE the covering
/// scope carries. So a person who is LocalAdmin in organisation A and only LocalHR in organisation B passed the
/// check for an employee in B, through the LocalHR scope, although the route needs LocalAdmin power. The fix
/// passes LocalAdmin as the role floor: a scope below the floor never admits. The actor then gets the route's
/// existing 403 ("Access denied"), and nothing is calculated, exported or recorded.</para>
///
/// <para><b>Expected values come from the spec</b> (the SECURITY.md invariant: every admin-policy route passes
/// its policy's role as the floor), not from what the code does today.</para>
///
/// <para><b>Fixture.</b> Two organisations (A = <c>STY_S145_W1A</c>, B = <c>STY_S145_W1B</c>) and a target
/// employee in B with no approved period. The attacker holds <c>LocalAdmin@A</c> and <c>LocalHR@B</c> (primary
/// role LocalAdmin, so the route's policy passes). The control holds <c>LocalAdmin@B</c>. With no APPROVED
/// period seeded, a request that clears the scope check answers the route's NEXT refusal, a 403 whose
/// <c>error</c> is "Period must be approved before payroll export". Both refusals are 403, so the facts read
/// the body's <c>error</c>, never the status alone.</para>
///
/// <para><b>Docker-gated, CI-verified, never locally green.</b></para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollCalculateScopeFloorTests : IAsyncLifetime
{
    private const string OrgA = "STY_S145_W1A";
    private const string OrgB = "STY_S145_W1B";
    private const string OkVersion = "OK24";
    private const string ScopeDenied = "Access denied";
    private const string ApprovalRefusal = "Period must be approved before payroll export";

    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    private TestFixtures.DockerHarness _harness = null!;
    private PayrollHostFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        // Organisation A exists once, for the whole class (the anchor employee creates its row).
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, "EMP-S145-W1-ORGA-ANCHOR", OrgA, "AC", OkVersion);
        _factory = new PayrollHostFactory(_harness.ConnectionString, new RuleRegistry().GetAll());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>W1-a.</b> LocalAdmin on org A plus LocalHR on org B, target employee in org B:
    /// <c>/calculate-and-export</c> answers 403 with <c>error = "Access denied"</c> (the scope refusal, not the
    /// approval refusal), and writes no <c>segment_manifests</c> row and no <c>payroll_export_records</c> row
    /// for the target.
    ///
    /// <para><b>Red conditions.</b> The route's scope check calls the UNFLOORED
    /// <c>OrgScopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct)</c>. Today the LocalHR@B scope
    /// covers org B and admits the actor, so the request moves on to the approval read and answers 403
    /// <c>"Period must be approved before payroll export"</c>: the status is still 403, and the
    /// <c>Assert.Equal("Access denied", error)</c> assertion trips on the body. The named MUTATION is the
    /// fix reverted to the no-floor call (remove the <c>StatsTidRoles.LocalAdmin</c> argument in the
    /// "Resource-scope guard (TASK-1902)" block of the Payroll <c>Program.cs</c>). CI-verified (Docker).</para>
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_LocalAdminElsewhereButOnlyLocalHrOverTarget_Denied403_WritesNothing()
    {
        const string employeeId = "EMP-S145-W1-DENIED";
        await SeedTargetEmployeeAsync(employeeId);

        var rsp = await PostCalculateAndExportAsync(MixedAdminHrToken("ACTOR-S145-W1-MIXED"), employeeId);
        var raw = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(ScopeDenied, body.GetProperty("error").GetString());

        Assert.Equal(0, await CountManifestsAsync(employeeId));
        Assert.Equal(0, await CountExportRecordsAsync(employeeId));
    }

    /// <summary>
    /// <b>W1-b (control).</b> A LocalAdmin whose own scope is on org B (the target's org) passes the scope
    /// check: the route answers its NEXT refusal, 403 <c>"Period must be approved before payroll export"</c>
    /// (no APPROVED period is seeded), and in particular NOT the scope refusal. Proves the floor admits a
    /// correctly scoped LocalAdmin.
    ///
    /// <para><b>Red conditions.</b> Passes today and must keep passing. It trips if the floor is built wrongly
    /// (for example a floor above LocalAdmin, or a floored call that ignores a LocalAdmin scope on the target):
    /// the body's <c>error</c> would be "Access denied".</para>
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_LocalAdminOverTarget_PassesScopeCheck_AnswersApprovalRefusal()
    {
        const string employeeId = "EMP-S145-W1-ALLOWED";
        await SeedTargetEmployeeAsync(employeeId);

        var rsp = await PostCalculateAndExportAsync(LocalAdminOnBToken("ACTOR-S145-W1-ADMIN-B"), employeeId);
        var raw = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected the approval 403, got {(int)rsp.StatusCode}: {raw}");
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(ApprovalRefusal, body.GetProperty("error").GetString());
    }

    // ── HTTP ──

    private async Task<HttpResponseMessage> PostCalculateAndExportAsync(string bearer, string employeeId)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(new
            {
                profile = new EmploymentProfile
                {
                    EmployeeId = employeeId,
                    AgreementCode = "AC",
                    OkVersion = OkVersion,
                    EmploymentCategory = "Standard",
                    PartTimeFraction = 1.0m,
                    OrgId = OrgB,
                },
                periodStart = Mar01,
                periodEnd = Mar31,
                // S146 R-14: no opening balance in the body — it comes from the recorded ledger; any value is now a 400.
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    /// <summary>LocalAdmin@A (primary, so the route's policy passes) + LocalHR@B (the covering, below-floor scope).</summary>
    private static string MixedAdminHrToken(string actorId) =>
        NewTokenService().GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalAdmin,
            agreementCode: "AC", orgId: OrgA,
            scopes: new[]
            {
                new RoleScope(StatsTidRoles.LocalAdmin, OrgA, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalHR, OrgB, "ORG_ONLY"),
            });

    private static string LocalAdminOnBToken(string actorId) =>
        NewTokenService().GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalAdmin,
            agreementCode: "AC", orgId: OrgB,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, OrgB, "ORG_ONLY") });

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = PayrollHostFactory.DevSigningKey,
        ExpirationMinutes = 60,
    });

    // ── Seeding and reads ──

    /// <summary>The target employee belongs to org B (the row is created here; org A exists since setup).</summary>
    private async Task SeedTargetEmployeeAsync(string employeeId)
        => await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgB, "AC", OkVersion);

    private async Task<int> CountManifestsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountExportRecordsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
