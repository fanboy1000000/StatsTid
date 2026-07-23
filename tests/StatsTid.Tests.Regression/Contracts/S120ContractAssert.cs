using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S120 / TASK-12002 — tiny shared helpers for the Pass-7 bucket-C per-route spec≡runtime
/// classes (<c>S120TimeSpecRuntimeTests</c> / <c>S120BalanceSpecRuntimeTests</c> /
/// <c>S120ComplianceSpecRuntimeTests</c> / <c>S120OvertimeSpecRuntimeTests</c> /
/// <c>S120SkemaSpecRuntimeTests</c>):
///
/// <list type="bullet">
///   <item><description><see cref="ActorClient"/> — a client for an arbitrary actor/role/scope
///     set (employee-self positive floor pins, leader floor pins, foreign-employee 403 pins).
///     Mirrors the Support helper's JWT minting; Support itself consumed AS-IS.</description></item>
///   <item><description><see cref="AssertUnconditioned"/> — the S119 doubly-pinned
///     UNCONDITIONED-mutation precedent (Step-0b Reviewer N2): the bucket-C mutations carry NO
///     If-Match/If-None-Match surface. The calling test sends NO precondition header (pin half 1:
///     the mutation must SUCCEED header-free) and this assert pins half 2: the response serves NO
///     ETag. If any of these ops ever grows a concurrency surface, the pin goes RED.</description></item>
///   <item><description><see cref="ExecAsync"/> — one-statement seed convenience for the
///     S120-prefixed INPUT rows (projection rows, overtime_balances, compensatory_rest, flex
///     events — input data, never derived state; settlement states are always driven through the
///     REAL settle machinery per the S117 rule).</description></item>
/// </list>
///
/// <para><see cref="SpecRuntimeMatcher"/> + <see cref="SpecRuntimeTestSupport"/> +
/// <see cref="S118ContractAssert"/> (exact-key-set) + <see cref="S119ContractAssert"/>
/// (AssertNoEtag) are consumed AS-IS — this file only ADDS sibling helpers; no existing test
/// file is modified.</para>
/// </summary>
internal static class S120ContractAssert
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>A client for an arbitrary actor/role/scope set on the given host.</summary>
    public static HttpClient ActorClient(
        WebApplicationFactory<Program> app,
        string actorId, string role, string orgId, params RoleScope[] scopes)
    {
        var client = app.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: actorId, name: actorId, role: role,
            agreementCode: "AC", orgId: orgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>An employee-self client (ORG_ONLY Employee scope) — the positive
    /// EmployeeOrAbove floor pin actor for the bucket's 16 EmployeeOrAbove ops.</summary>
    public static HttpClient EmployeeClient(WebApplicationFactory<Program> app, string employeeId, string orgId)
        => ActorClient(app, employeeId, StatsTidRoles.Employee, orgId,
            new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY"));

    /// <summary>An in-scope leader client (ORG_ONLY LocalLeader scope) — the LeaderOrAbove
    /// floor pin actor for the compensate POST.</summary>
    public static HttpClient LeaderClient(WebApplicationFactory<Program> app, string actorId, string orgId)
        => ActorClient(app, actorId, StatsTidRoles.LocalLeader, orgId,
            new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY"));

    /// <summary>The UNCONDITIONED-mutation pin (the S119 doubly-pinned precedent): the calling
    /// test already sent the mutation WITHOUT any precondition header and asserted success; this
    /// half asserts the response carries NO ETag header.</summary>
    public static void AssertUnconditioned(HttpResponseMessage response, string context)
        => S119ContractAssert.AssertNoEtag(response, context);

    /// <summary>One parameterized non-query statement against the harness DB (INPUT-data seeds
    /// only — S120-prefixed ids, disjoint from every existing suite's census).</summary>
    public static async Task ExecAsync(
        string connectionString, string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
