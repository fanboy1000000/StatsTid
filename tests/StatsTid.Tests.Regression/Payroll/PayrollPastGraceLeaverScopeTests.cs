using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S147 / TASK-14725-T (Step 7a W-1; owner rulings 2026-11-05) — payroll keeps working for a leaver in the window
/// between the end of the grace (D+6) and the leaver job's switch-off.
///
/// <para><b>Plain language.</b> The fix makes a leaver "switched off" by DATE from day 6 on the routes managers use.
/// Payroll must not be caught by it: the leaver's final month still has to be paid, and the export route is for
/// LocalAdmin and above, never leaders. This route asks the terminated-inclusive scope check with a LocalAdmin
/// floor, so a LocalAdmin or GlobalAdmin is admitted for a leaver who is past the grace by date but still active.</para>
///
/// <para><b>Why the assertion is "the scope check admits", not a full export.</b> A full export needs an APPROVED
/// period, the recorded hours behind it and the Rule Engine stub to calculate a result; none of that is what this
/// task changes. As in <see cref="PayrollCalculateScopeFloorTests"/>, with no APPROVED period seeded a request
/// that clears the scope check answers the route's NEXT refusal, 403 whose <c>error</c> is "Period must be approved
/// before payroll export", whereas a scope refusal is 403 with <c>error</c> "Access denied". Both are 403, so the
/// fact reads the body's <c>error</c>, never the status alone.</para>
///
/// <para><b>Kind R — regression pin, holds today on master <c>c13024cd</c></b> (payroll admission is kept, not
/// changed), and it is also the pin that stops the fix-up's validator rule from over-reaching onto payroll.
/// <b>Red conditions:</b> <c>Assert.Equal(ApprovalRefusal, error)</c> trips if the terminated-inclusive overload ever
/// treats the past-grace-while-active target as "not found" or refuses a LocalAdmin / GlobalAdmin scope (the body
/// would then say "Access denied"). Named mutation: in <c>OrgScopeValidator</c>'s terminated-inclusive overload,
/// make the LocalHR-or-above floor reject a LocalAdmin scope for <c>pastGraceWhileActive</c>.</para>
///
/// <para><b>Docker</b> — CI-pending, never locally green. The Payroll host's clock is pinned to
/// <see cref="Today"/> through a <c>TimeProvider</c> override.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollPastGraceLeaverScopeTests : IAsyncLifetime
{
    private const string Org = "STY_S147_G5";
    private const string OkVersion = "OK24";
    private const string ApprovalRefusal = "Period must be approved before payroll export";

    private static readonly DateOnly Today = new(2026, 6, 10);
    private static readonly DateOnly May01 = new(2026, 5, 1);
    private static readonly DateOnly May31 = new(2026, 5, 31);

    private TestFixtures.DockerHarness _harness = null!;
    private PayrollHostFactory _factory = null!;
    private WebApplicationFactory<RetroactiveCorrectionService> _host = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new PayrollHostFactory(_harness.ConnectionString, new RuleRegistry().GetAll());
        _host = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Today));
        }));
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// A leaver whose last day was 6 days before today (D+6) and who is still <c>is_active = TRUE</c>: a LocalAdmin
    /// in the leaver's organisation and a GlobalAdmin each pass the scope check (the route answers the approval
    /// refusal, not "Access denied"). Kind R, see the class remarks.
    /// </summary>
    [Fact]
    public async Task Day6_LeaverStillActive_LocalAdminAndGlobalAdmin_PassTheScopeCheck()
    {
        const string employeeId = "EMP-S147-G5-LEAVER";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, Org, "AC", OkVersion);
        await SetEndDateAsync(employeeId, Today.AddDays(-6));

        foreach (var (label, token) in new[] { ("LocalAdmin", LocalAdminToken()), ("GlobalAdmin", GlobalAdminToken()) })
        {
            var rsp = await PostCalculateAndExportAsync(token, employeeId);
            var raw = await rsp.Content.ReadAsStringAsync();

            Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"{label}: expected the approval 403, got {(int)rsp.StatusCode}: {raw}");
            Assert.Equal(ApprovalRefusal, JsonDocument.Parse(raw).RootElement.GetProperty("error").GetString());
        }
    }

    // ── HTTP and tokens ──

    private async Task<HttpResponseMessage> PostCalculateAndExportAsync(string bearer, string employeeId)
    {
        var client = _host.CreateClient();
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
                    OrgId = Org,
                },
                periodStart = May01,
                periodEnd = May31,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    private static string LocalAdminToken() =>
        NewTokenService().GenerateToken(
            employeeId: "ACTOR-S147-G5-ADMIN", name: "ACTOR-S147-G5-ADMIN", role: StatsTidRoles.LocalAdmin,
            agreementCode: "AC", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, Org, "ORG_ONLY") });

    private static string GlobalAdminToken() =>
        NewTokenService().GenerateToken(
            employeeId: "ACTOR-S147-G5-GLOBAL", name: "ACTOR-S147-G5-GLOBAL", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = PayrollHostFactory.DevSigningKey,
        ExpirationMinutes = 60,
    });

    /// <summary>Sets the last day and leaves <c>is_active</c> TRUE: the window before the leaver job's switch-off.</summary>
    private async Task SetEndDateAsync(string userId, DateOnly end)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var batch = new NpgsqlBatch(conn);
        var cmd = new NpgsqlBatchCommand("UPDATE users SET employment_end_date = @d WHERE user_id = @id");
        cmd.Parameters.AddWithValue("d", end);
        cmd.Parameters.AddWithValue("id", userId);
        batch.BatchCommands.Add(cmd);
        Assert.Equal(1, await batch.ExecuteNonQueryAsync());
    }
}
