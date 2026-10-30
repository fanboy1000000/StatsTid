using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Overtime;

// S147 / TASK-14705-T — SEC-056, the self part (owner Q-A = (a), T9): nobody approves, rejects or
// pays out their OWN overtime. Expected values come from the refinement (T9, Q-A, AC-056-1 … -3), not
// from the code. The actor is an ACTIVE LocalLeader whose ORG_ONLY scope covers their own
// organisation, with no employment end date: this is the segregation rule, not the leaver rule.

/// <summary>
/// One Postgres testcontainer + ONE booted API (a derived host with an in-memory log capture) shared
/// by every Docker fact of <see cref="OvertimeSelfDecisionTests"/> (a class fixture, so the ~7 facts do
/// not each boot a container). Seeds a disjoint organisation tree and two ACTIVE LocalLeaders:
/// <see cref="Self"/> (the actor in the self cases, whose OWN rows are the target) and
/// <see cref="Other"/> (a second leader whose scope covers the same organisation).
/// </summary>
public sealed class OvertimeSelfDecisionFixture : IAsyncLifetime
{
    private const string DevSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    public const string Mao = "S147SMAO";
    public const string Org = "S147S01";
    public const string Self = "s147s_self";     // active LocalLeader; the target of their own overtime rows
    public const string Other = "s147s_other";   // active LocalLeader, same organisation scope

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _root = null!;
    private WebApplicationFactory<Program> _host = null!;

    public string ConnectionString { get; private set; } = null!;
    public CapturingLogProvider Logs { get; } = new();

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
        _root = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _host = _root.WithWebHostBuilder(b => b.ConfigureLogging(l => l.AddProvider(Logs)));
        _ = _host.CreateClient(); // boot the host seeders (baseline org tree) BEFORE seeding ours

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('S147SMAO', 'S147 Selv Ministerie', 'MAO',          NULL,       '/S147SMAO/',          'AC', 'OK24'),
                ('S147S01',  'S147 Selv Styrelse',    'ORGANISATION', 'S147SMAO', '/S147SMAO/S147S01/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
            await cmd.ExecuteNonQueryAsync();

        // Two ACTIVE leaders (is_active TRUE, employment_end_date NULL), both homed in S147S01.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                ('s147s_self',  's147s_self',  '$2a$11$fake', 'S147 Selv Leder',  's147s_self@test.dk',  'S147S01', 'HK', 'OK24', TRUE),
                ('s147s_other', 's147s_other', '$2a$11$fake', 'S147 Anden Leder', 's147s_other@test.dk', 'S147S01', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
            await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _root?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>A client for an ACTIVE LocalLeader whose ORG_ONLY scope covers <see cref="Org"/>.</summary>
    public HttpClient LeaderClient(string actorId)
    {
        var client = _host.CreateClient();
        var tokens = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokens.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalLeader,
            agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, Org, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<long> CountAsync(string table)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        // Constant SQL per allowed table (no interpolation: keeps CA2100 quiet and the table list closed).
        var sql = table switch
        {
            "audit_log" => "SELECT COUNT(*) FROM audit_log",
            "audit_projection" => "SELECT COUNT(*) FROM audit_projection",
            "outbox_events" => "SELECT COUNT(*) FROM outbox_events",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "unsupported table"),
        };
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Counts the HTTP-journal rows (<c>audit_log</c>) one actor has with a given status and result.
    /// Constant SQL with parameters (no interpolation, so CA2100 stays quiet). The journal middleware
    /// appends AFTER the endpoint returns; the bounded poll (up to 5 s) is a belt-and-braces guard: it
    /// returns as soon as the count reaches <paramref name="atLeast"/>, else the last count seen.
    /// </summary>
    public async Task<long> CountAuditLogAsync(string actorId, int httpStatus, string result, long atLeast = 0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM audit_log WHERE actor_id = @a AND http_status = @s AND result = @r", conn);
            cmd.Parameters.AddWithValue("a", actorId);
            cmd.Parameters.AddWithValue("s", httpStatus);
            cmd.Parameters.AddWithValue("r", result);
            var n = (long)(await cmd.ExecuteScalarAsync())!;
            if (n >= atLeast || DateTime.UtcNow >= deadline) return n;
            await Task.Delay(50);
        }
    }

    public async Task<(string Status, string? ApprovedBy)> ReadPreApprovalAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT status, approved_by FROM overtime_pre_approvals WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), $"pre-approval {id} must exist");
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
    }

    public sealed record CapturedLog(string Category, LogLevel Level, string Message);

    public sealed class CapturingLogProvider : ILoggerProvider
    {
        private readonly List<CapturedLog> _records = new();
        private readonly object _gate = new();
        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, this);
        public void Dispose() { }
        public IReadOnlyList<CapturedLog> Snapshot() { lock (_gate) return _records.ToArray(); }

        private sealed class Capture(string category, CapturingLogProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._gate) owner._records.Add(new CapturedLog(category, logLevel, formatter(state, exception)));
            }
        }
    }
}

/// <summary>
/// S147 / TASK-14705-T — the overtime self-decision pins (Docker; CI-pending until CI-W2). A
/// differential matrix: the SAME three decisions (approve / reject / compensate) by an active leader on
/// THEIR OWN overtime (403) and by ANOTHER leader in scope (200).
///
/// <para><b>Reading the kinds.</b> "F" = fails on the code before TASK-14705 (the production behaviour
/// that fails today is named under "Red conditions"); "R" = a regression pin (an existing behaviour
/// kept). Docker facts cannot run locally, so they are reported CI-pending.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OvertimeSelfDecisionTests : IClassFixture<OvertimeSelfDecisionFixture>
{
    private const string SelfDenialLogCategory = "StatsTid.Backend.Api.Endpoints.OvertimeEndpoints.SelfGuard";
    private const string PreApprovalReason = "self-decision on an overtime pre-approval is not permitted (segregation of duties)";
    private const string CompensateReason = "self-decision on overtime compensation is not permitted (segregation of duties)";

    private readonly OvertimeSelfDecisionFixture _fx;
    public OvertimeSelfDecisionTests(OvertimeSelfDecisionFixture fx) => _fx = fx;

    /// <summary>
    /// <b>AC-056-1 (F) — approve.</b> A leader approving a pre-approval that names THEMSELVES is refused
    /// (403, the exact date-free reason), the row stays PENDING with no approver; another leader in scope
    /// approving the same kind of row gets 200 and is stamped as the approver.
    /// <para><b>Red conditions:</b> before TASK-14705 the approve handler only checked org scope and
    /// PENDING, so the self-approve answered 200 and stamped the actor as approver — the 403 assertion
    /// and the "still PENDING / approved_by null" assertions fail today.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task Approve_SelfIs403_AnotherLeaderInScopeIs200()
    {
        using var self = _fx.LeaderClient(OvertimeSelfDecisionFixture.Self);
        using var other = _fx.LeaderClient(OvertimeSelfDecisionFixture.Other);

        var selfRow = await CreateAsync(other, OvertimeSelfDecisionFixture.Self, "2026-01-01", "2026-01-31");
        using var denied = await PutAsync(self, $"/api/overtime/pre-approval/{selfRow}/approve", """{ "reason": "mig selv" }""");
        await AssertDeniedAsync(denied, PreApprovalReason);
        var after = await _fx.ReadPreApprovalAsync(selfRow);
        Assert.Equal("PENDING", after.Status);
        Assert.Null(after.ApprovedBy);

        // The differential: the same row, decided by the OTHER leader in scope, goes through.
        using var ok = await PutAsync(other, $"/api/overtime/pre-approval/{selfRow}/approve", """{ "reason": "godkendt af anden" }""");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var approved = await _fx.ReadPreApprovalAsync(selfRow);
        Assert.Equal("APPROVED", approved.Status);
        Assert.Equal(OvertimeSelfDecisionFixture.Other, approved.ApprovedBy);
    }

    /// <summary>
    /// <b>AC-056-1 (F) — reject.</b> Self reject → 403 (exact reason), row stays PENDING; another leader in
    /// scope rejecting the row → 200 and REJECTED.
    /// <para><b>Red conditions:</b> before TASK-14705 the reject handler checked only org scope and PENDING,
    /// so the self-reject answered 200 and flipped the row to REJECTED — the 403 and "still PENDING"
    /// assertions fail today.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task Reject_SelfIs403_AnotherLeaderInScopeIs200()
    {
        using var self = _fx.LeaderClient(OvertimeSelfDecisionFixture.Self);
        using var other = _fx.LeaderClient(OvertimeSelfDecisionFixture.Other);

        var selfRow = await CreateAsync(other, OvertimeSelfDecisionFixture.Self, "2026-02-01", "2026-02-28");
        using var denied = await PutAsync(self, $"/api/overtime/pre-approval/{selfRow}/reject", """{ "reason": "mig selv" }""");
        await AssertDeniedAsync(denied, PreApprovalReason);
        Assert.Equal("PENDING", (await _fx.ReadPreApprovalAsync(selfRow)).Status);

        using var ok = await PutAsync(other, $"/api/overtime/pre-approval/{selfRow}/reject", """{ "reason": "afvist af anden" }""");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("REJECTED", (await _fx.ReadPreApprovalAsync(selfRow)).Status);
    }

    /// <summary>
    /// <b>AC-056-1 (F) — compensate.</b> The balance row is seeded DIRECTLY with 10 accumulated hours (no
    /// production write feeds the balance today; this is test seeding). A leader compensating THEIR OWN
    /// balance — PAYOUT and AFSPADSERING — is refused (403, exact reason) and <c>paid_out</c> /
    /// <c>afspadsering_used</c> stay 0 / 0; another leader in scope compensating the same balance gets 200
    /// for both, moving the figures to 2 and 3.
    /// <para><b>Red conditions:</b> before TASK-14705 the compensate handler checked only org scope, so the
    /// self PAYOUT answered 200 and moved <c>paid_out</c> to 2 — the 403 and the "unchanged 0/0"
    /// assertions fail today.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task Compensate_SelfIs403AndBalanceUnchanged_AnotherLeaderInScopeIs200()
    {
        const int year = 2026;
        await using (var conn = new NpgsqlConnection(_fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO overtime_balances (employee_id, agreement_code, period_year, accumulated, paid_out, afspadsering_used)
                VALUES (@e, 'HK', @y, 10, 0, 0)
                ON CONFLICT (employee_id, period_year) DO UPDATE SET accumulated = 10, paid_out = 0, afspadsering_used = 0
                """, conn);
            cmd.Parameters.AddWithValue("e", OvertimeSelfDecisionFixture.Self);
            cmd.Parameters.AddWithValue("y", year);
            await cmd.ExecuteNonQueryAsync();
        }

        using var self = _fx.LeaderClient(OvertimeSelfDecisionFixture.Self);
        using var other = _fx.LeaderClient(OvertimeSelfDecisionFixture.Other);
        var url = $"/api/overtime/{OvertimeSelfDecisionFixture.Self}/compensate";

        using (var denied = await PostAsync(self, url, $$"""{ "periodYear": {{year}}, "hours": 2, "compensationType": "PAYOUT" }"""))
            await AssertDeniedAsync(denied, CompensateReason);
        using (var denied = await PostAsync(self, url, $$"""{ "periodYear": {{year}}, "hours": 3, "compensationType": "AFSPADSERING" }"""))
            await AssertDeniedAsync(denied, CompensateReason);
        Assert.Equal((0m, 0m), await ReadBalanceAsync(OvertimeSelfDecisionFixture.Self, year));

        using (var ok = await PostAsync(other, url, $$"""{ "periodYear": {{year}}, "hours": 2, "compensationType": "PAYOUT" }"""))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var ok = await PostAsync(other, url, $$"""{ "periodYear": {{year}}, "hours": 3, "compensationType": "AFSPADSERING" }"""))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal((2m, 3m), await ReadBalanceAsync(OvertimeSelfDecisionFixture.Self, year));
    }

    /// <summary>
    /// <b>AC-056-2 (F) — the guard fires BEFORE the status check.</b> A leader approving, and rejecting, a
    /// pre-approval of their own that is NOT pending (APPROVED / REJECTED) gets 403, not the state-leaking
    /// 400 "already …". The denial writes a WARNING under the self-guard log category naming the actor and
    /// the row. It writes no <c>audit_projection</c> row, no <c>outbox_events</c> row and no event (the
    /// "nothing was decided" proof; a successful decision by the other leader is the positive control that
    /// proves the audit-projection counter can move). The HTTP journal (<c>audit_log</c>, one row per
    /// request via <c>AuditLoggingMiddleware</c>, 403s included) is a different thing: it records each
    /// refused request, so the two refusals add exactly two rows for the refusing actor, each
    /// <c>http_status = 403</c> and <c>result = 'failure'</c> (counted per actor, around the two calls).
    /// <para><b>Red conditions</b> (against the pre-TASK-14705 baseline <c>9de1ae79</c>): there a self
    /// approve/reject on a non-PENDING row answered 400 ("Pre-approval is already …", the status check ran
    /// first) — the 403 assertions fail, the journal rows carry 400 so the +2 count of 403 failures does
    /// not hold, and no WARNING under the category exists. (The refinement's AC-056-2 words it "403, not
    /// 409"; the route answered 400 for a non-PENDING row, so the pin is 403, not 400 — TASK-14705's brief,
    /// BC-P5.) Kind F; the AC's "no audit row" is restated here as "no audit_projection / outbox / event row".</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task SelfDecision_OnNonPendingRow_Is403Not400_LogsWarning_JournalsTwoRefusals_WritesNoDecisionTrail()
    {
        using var self = _fx.LeaderClient(OvertimeSelfDecisionFixture.Self);
        using var other = _fx.LeaderClient(OvertimeSelfDecisionFixture.Other);

        var approvedRow = await CreateAsync(other, OvertimeSelfDecisionFixture.Self, "2026-03-01", "2026-03-31");
        var rejectedRow = await CreateAsync(other, OvertimeSelfDecisionFixture.Self, "2026-04-01", "2026-04-30");
        var controlRow = await CreateAsync(other, OvertimeSelfDecisionFixture.Self, "2026-05-01", "2026-05-31");

        // Positive control: a legitimate decision DOES add exactly one audit_projection row.
        var projBeforeControl = await _fx.CountAsync("audit_projection");
        using (var ok = await PutAsync(other, $"/api/overtime/pre-approval/{controlRow}/approve", """{ "reason": "kontrol" }"""))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(projBeforeControl + 1, await _fx.CountAsync("audit_projection"));

        using (var ok = await PutAsync(other, $"/api/overtime/pre-approval/{approvedRow}/approve", """{ "reason": "godkendt" }"""))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var ok = await PutAsync(other, $"/api/overtime/pre-approval/{rejectedRow}/reject", """{ "reason": "afvist" }"""))
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var journalBefore = await _fx.CountAuditLogAsync(OvertimeSelfDecisionFixture.Self, 403, "failure");
        var projBefore = await _fx.CountAsync("audit_projection");
        var outboxBefore = await _fx.CountAsync("outbox_events");

        using (var denied = await PutAsync(self, $"/api/overtime/pre-approval/{approvedRow}/approve", """{ "reason": "mig selv" }"""))
            await AssertDeniedAsync(denied, PreApprovalReason);
        using (var denied = await PutAsync(self, $"/api/overtime/pre-approval/{rejectedRow}/reject", """{ "reason": "mig selv" }"""))
            await AssertDeniedAsync(denied, PreApprovalReason);

        // The HTTP journal records the two refused requests: exactly two 403 failures for the refusing actor.
        Assert.Equal(journalBefore + 2, await _fx.CountAuditLogAsync(OvertimeSelfDecisionFixture.Self, 403, "failure", journalBefore + 2));
        Assert.Equal(projBefore, await _fx.CountAsync("audit_projection"));
        Assert.Equal(outboxBefore, await _fx.CountAsync("outbox_events"));
        Assert.Equal("APPROVED", (await _fx.ReadPreApprovalAsync(approvedRow)).Status);
        Assert.Equal("REJECTED", (await _fx.ReadPreApprovalAsync(rejectedRow)).Status);

        var warnings = _fx.Logs.Snapshot()
            .Where(l => l.Category == SelfDenialLogCategory && l.Level == LogLevel.Warning)
            .Select(l => l.Message).ToList();
        Assert.Contains(warnings, m =>
            m.Contains("SEC-056 self-decision denied", StringComparison.Ordinal)
            && m.Contains(OvertimeSelfDecisionFixture.Self, StringComparison.Ordinal)
            && m.Contains(approvedRow.ToString(), StringComparison.Ordinal)
            && m.Contains("(approve)", StringComparison.Ordinal));
        Assert.Contains(warnings, m =>
            m.Contains("SEC-056 self-decision denied", StringComparison.Ordinal)
            && m.Contains(rejectedRow.ToString(), StringComparison.Ordinal)
            && m.Contains("(reject)", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>AC-056-3 (R) — regression pin: a request is not a decision.</b> An ACTIVE leader creating a
    /// pre-approval that names THEMSELVES still gets the route's success status (201 — the true-201 create
    /// the S116 spec pins), and the row is PENDING, so someone else then approves it. Holds today and must
    /// keep holding: the self guard belongs on approve / reject / compensate, never on create.
    /// <para><b>Red conditions:</b> none today (a regression pin). It goes red if the guard is ever placed
    /// on the create route.</para>
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task Create_ForOneself_StillSucceeds_RowIsPending()
    {
        using var self = _fx.LeaderClient(OvertimeSelfDecisionFixture.Self);
        var id = await CreateAsync(self, OvertimeSelfDecisionFixture.Self, "2026-06-01", "2026-06-30");
        var row = await _fx.ReadPreApprovalAsync(id);
        Assert.Equal("PENDING", row.Status);
        Assert.Null(row.ApprovedBy);
    }

    // ── Support ──

    private static async Task AssertDeniedAsync(HttpResponseMessage response, string expectedReason)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Forbidden == response.StatusCode, $"expected 403, got {(int)response.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "error", "reason" }, props); // date-free: nothing but the two fields
        Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(expectedReason, doc.RootElement.GetProperty("reason").GetString());
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string employeeId, string start, string end)
    {
        using var response = await client.PostAsync("/api/overtime/pre-approval", Json(
            $$"""{ "employeeId": "{{employeeId}}", "periodStart": "{{start}}", "periodEnd": "{{end}}", "maxHours": 10, "reason": "S147 SEC-056" }"""));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == response.StatusCode, $"create for {employeeId} expected 201, got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<(decimal PaidOut, decimal AfspadseringUsed)> ReadBalanceAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT paid_out, afspadsering_used FROM overtime_balances WHERE employee_id = @e AND period_year = @y", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), "balance row must exist");
        return (r.GetDecimal(0), r.GetDecimal(1));
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
    private static Task<HttpResponseMessage> PutAsync(HttpClient c, string url, string body) => c.PutAsync(url, Json(body));
    private static Task<HttpResponseMessage> PostAsync(HttpClient c, string url, string body) => c.PostAsync(url, Json(body));
}

/// <summary>
/// S147 / TASK-14705-T — the unit-level pins of the generalised self predicate
/// <c>ApprovalSelfGuard.IsSelf(ActorContext, string)</c> and its period overload. Runs locally (not
/// Docker). The helper is <c>internal</c> to <c>StatsTid.Backend.Api</c>, which declares no
/// <c>InternalsVisibleTo</c> (and tests may not edit <c>src/**</c>), so the facts reach it by
/// reflection; the public <see cref="ActorContext"/> / <see cref="ApprovalPeriod"/> types are real.
///
/// <para><b>Kind N</b> (the facts name a member this sprint creates — the string overload). Each fact
/// proves its RED by a named mutation at the wave gate (recorded in the TASK-14705-T report), never by
/// reverting the implementation: M1 "drop the null/empty actor-id short-circuit" turns
/// <see cref="IsSelf_NullOrEmptyActor_NeverMatches"/> red; M2 "compare OrdinalIgnoreCase" turns
/// <see cref="IsSelf_IsOrdinal_CaseDiffers_IsNotSelf"/> red; M3 "return false" turns
/// <see cref="IsSelf_SameId_IsSelf"/> and the period-overload fact red (each run at the wave gate: M1 → 1 failed, M2 → 1 failed, M3 → 2 failed).
/// <see cref="IsSelf_DifferentId_IsNotSelf"/> is the boundary partner of the same-id fact. M4 "IsSelf
/// returns true" (the string overload's body replaced by <c>=&gt; true</c>, run in a detached worktree at
/// fix-up 1): 4 failed, 1 passed — <see cref="IsSelf_DifferentId_IsNotSelf"/> failed with
/// "Assert.False() Failure / Expected: False / Actual: True" (so did the null/empty, ordinal and period
/// facts; only the same-id fact stayed green, as it should).</para>
/// </summary>
public sealed class ApprovalSelfGuardIsSelfTests
{
    private static readonly Type Guard = typeof(Program).Assembly.GetType("StatsTid.Backend.Api.Endpoints.Helpers.ApprovalSelfGuard")
        ?? throw new InvalidOperationException("ApprovalSelfGuard not found in StatsTid.Backend.Api");

    private static bool IsSelf(string? actorId, string employeeId)
    {
        var m = Guard.GetMethod("IsSelf", BindingFlags.Public | BindingFlags.Static, new[] { typeof(ActorContext), typeof(string) })
            ?? throw new InvalidOperationException("IsSelf(ActorContext, string) not found");
        return (bool)m.Invoke(null, new object?[] { Actor(actorId), employeeId })!;
    }

    private static bool IsSelfPeriod(string? actorId, ApprovalPeriod period)
    {
        var m = Guard.GetMethod("IsSelf", BindingFlags.Public | BindingFlags.Static, new[] { typeof(ActorContext), typeof(ApprovalPeriod) })
            ?? throw new InvalidOperationException("IsSelf(ActorContext, ApprovalPeriod) not found");
        return (bool)m.Invoke(null, new object?[] { Actor(actorId), period })!;
    }

    private static ActorContext Actor(string? id) => new(id, StatsTidRoles.LocalLeader, Guid.NewGuid());

    private static ApprovalPeriod Period(string employeeId) => new()
    {
        PeriodId = Guid.NewGuid(), EmployeeId = employeeId, OrgId = "S147S01",
        PeriodStart = new DateOnly(2026, 1, 1), PeriodEnd = new DateOnly(2026, 1, 31),
        PeriodType = "MONTHLY", Status = "SUBMITTED", AgreementCode = "HK", OkVersion = "OK24",
    };

    /// <summary>Kind N. The actor id equals the employee id → self.</summary>
    [Fact]
    public void IsSelf_SameId_IsSelf() => Assert.True(IsSelf("emp_1", "emp_1"));

    /// <summary>Kind N. A different employee id → not self.</summary>
    [Fact]
    public void IsSelf_DifferentId_IsNotSelf() => Assert.False(IsSelf("emp_1", "emp_2"));

    /// <summary>Kind N. The comparison is Ordinal: ids that differ only by case are different people.</summary>
    [Fact]
    public void IsSelf_IsOrdinal_CaseDiffers_IsNotSelf() => Assert.False(IsSelf("Emp_1", "emp_1"));

    /// <summary>Kind N. A null or empty actor id never matches — even against an empty employee id, and
    /// (for null) even though <c>string.Equals(null, null)</c> would be true.</summary>
    [Fact]
    public void IsSelf_NullOrEmptyActor_NeverMatches()
    {
        Assert.False(IsSelf(null, ""));
        Assert.False(IsSelf("", ""));
        Assert.False(IsSelf(null, "emp_1"));
    }

    /// <summary>Kind R. The period overload keeps its behaviour by delegating: own period → self; another
    /// employee's period → not self; null actor → not self.</summary>
    [Fact]
    public void IsSelf_PeriodOverload_DelegatesWithUnchangedBehaviour()
    {
        Assert.True(IsSelfPeriod("emp_1", Period("emp_1")));
        Assert.False(IsSelfPeriod("emp_1", Period("emp_2")));
        Assert.False(IsSelfPeriod(null, Period("")));
    }
}
