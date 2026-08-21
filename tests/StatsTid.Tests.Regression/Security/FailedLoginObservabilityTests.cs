using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S132 / TASK-132-3c-2 (SEC-040 ← QUAL-063) — <b>failed logins must be observable</b>.
///
/// <para><b>The gap (baseline):</b> a rejected <c>POST /api/auth/login</c> produced NO
/// application log line, and the attempted username reached no store — so ops could not
/// distinguish an <i>unknown-user</i> attempt from a <i>wrong-password</i> one, nor spot a
/// brute-force pattern. The only trace was the <c>AuditLoggingMiddleware</c> 401 row (IP +
/// null actor, no identifier).</para>
///
/// <para><b>The fix:</b> the login handler now emits ONE structured WARNING on every failure
/// carrying the attempted <b>username</b> (an identifier, never a secret), a failure-reason
/// <b>class</b> (<c>unknown_user</c> vs <c>invalid_password</c>), the source IP, and the request
/// CorrelationId (which joins the line to the audit_log 401 row). The password is NEVER logged.</para>
///
/// <para><b>RED-on-baseline:</b> both facts below assert an <c>AuthEndpoints</c>-category WARNING
/// exists carrying the attempted username + reason class. On the pre-fix code no such line is
/// emitted, so <see cref="Assert.Contains{T}(System.Collections.Generic.IEnumerable{T}, System.Predicate{T})"/>
/// against the (empty) set FAILS — the tests are RED on the old code and GREEN after.</para>
///
/// <para><b>Security invariants pinned:</b> (1) the password sentinel appears in NO captured log
/// line — we never log a secret; (2) both failure classes return an <i>identical</i> generic
/// <c>401</c> to the client, so the unknown-user/wrong-password distinction lives ONLY in the
/// server-side log and no user-enumeration oracle is exposed in the response.</para>
///
/// <para>Both handler branches are exercised: the in-memory dev-credential branch
/// (<c>Auth:UseDatabase=false</c>) covers BOTH reason classes with the built-in <c>admin01</c>
/// user; the DB/BCrypt PRODUCTION branch (<c>Auth:UseDatabase=true</c>) proves the real auth path
/// logs an unknown-user attempt too (no user seed needed — the repository read returns null).</para>
///
/// <para>Host/config timing mirrors <c>S118LoginSpecRuntimeTests</c>: the auth-mode + connection
/// string are injected at HOST configuration (<see cref="IHostBuilder.ConfigureHostConfiguration"/>
/// via <c>CreateHost</c>) because <c>Program.cs</c> reads them off <c>builder.Configuration</c>
/// BEFORE <c>Build()</c>. The capturing <see cref="ILoggerProvider"/> is added in the same place
/// so it is registered into the <c>ILoggerFactory</c> the login endpoint resolves at map time.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FailedLoginObservabilityTests : IAsyncLifetime
{
    private const string LoginPath = "/api/auth/login";
    private const string AuthEndpointsCategory = "StatsTid.Backend.Api.Endpoints.AuthEndpoints";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  In-memory dev-credential branch (Auth:UseDatabase=false) — BOTH reason classes.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FailedLogin_InMemoryBranch_LogsUsernameAndReason_NeverPassword()
    {
        const string unknownUser = "s132_unknown_user_probe";
        const string unknownPassword = "S132-unknown-secret-DO-NOT-LOG-a1b2c3";
        const string knownUser = "admin01"; // built-in dev-credential user
        const string wrongPassword = "S132-wrong-secret-DO-NOT-LOG-d4e5f6";

        await using var factory = new CapturingWebApplicationFactory(_harness.ConnectionString, useDbAuth: false);
        using var client = factory.CreateClient(); // login is anonymous — no bearer token

        // (1) Unknown user → 401 + an "unknown_user" WARNING carrying the attempted username.
        using var unknownResp = await client.PostAsJsonAsync(
            LoginPath, new { username = unknownUser, password = unknownPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, unknownResp.StatusCode);

        // (2) Known user, wrong password → the SAME generic 401 + an "invalid_password" WARNING.
        using var wrongPwResp = await client.PostAsJsonAsync(
            LoginPath, new { username = knownUser, password = wrongPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPwResp.StatusCode);

        var authWarnings = AuthWarnings(factory.Provider);

        // RED-on-baseline: no AuthEndpoints WARNING is emitted for a failed login on the old code.
        Assert.Contains(authWarnings, m => m.Contains(unknownUser) && m.Contains("unknown_user"));
        Assert.Contains(authWarnings, m => m.Contains(knownUser) && m.Contains("invalid_password"));

        // The distinction lives ONLY in the log — the client response is an identical 401 for both.
        Assert.Equal(unknownResp.StatusCode, wrongPwResp.StatusCode);

        // A secret must NEVER reach any log line, in ANY category.
        AssertNoSecretLogged(factory.Provider, unknownPassword, wrongPassword);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  DB/BCrypt PRODUCTION branch (Auth:UseDatabase=true) — the real auth path logs too.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FailedLogin_DbBranch_UnknownUser_LogsUsernameAndReason_NeverPassword()
    {
        const string unknownUser = "s132_db_ghost_probe";
        const string unknownPassword = "S132-db-secret-DO-NOT-LOG-fedcba";

        await using var factory = new CapturingWebApplicationFactory(_harness.ConnectionString, useDbAuth: true);
        using var client = factory.CreateClient(); // login is anonymous

        using var resp = await client.PostAsJsonAsync(
            LoginPath, new { username = unknownUser, password = unknownPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        var authWarnings = AuthWarnings(factory.Provider);

        // RED-on-baseline: the production auth path emitted no log line on a failed login.
        Assert.Contains(authWarnings, m => m.Contains(unknownUser) && m.Contains("unknown_user"));

        AssertNoSecretLogged(factory.Provider, unknownPassword);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Step-5a Codex P2 — CR/LF log-forging (CWE-117): the username VALUE is sanitized.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A structured <c>{Username}</c> placeholder stops message-TEMPLATE injection but does NOT
    /// escape the VALUE — so an attacker-supplied newline in the username would render as a
    /// FORGED second log line at a plain-text/console sink. This pins the <c>SanitizeForLog</c>
    /// fix: a failed login whose username carries <c>\r\n</c> still produces the observability
    /// WARNING, but NO captured <c>AuthEndpoints</c> line contains a raw CR/LF (no forged line);
    /// the value survives flattened to a single line. RED on the un-sanitized code, GREEN after.
    /// </summary>
    [Fact]
    public async Task FailedLogin_UsernameWithCrLf_IsSanitized_NoForgedLogLine()
    {
        const string forgedUsername = "attacker\r\n[FORGED] admin login ok";
        const string password = "S132-crlf-secret-DO-NOT-LOG-9z8y7x";

        await using var factory = new CapturingWebApplicationFactory(_harness.ConnectionString, useDbAuth: false);
        using var client = factory.CreateClient(); // login is anonymous

        using var resp = await client.PostAsJsonAsync(
            LoginPath, new { username = forgedUsername, password });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        var authWarnings = AuthWarnings(factory.Provider);

        // Observability is preserved — the failed-login WARNING is still emitted.
        Assert.Contains(authWarnings, m => m.Contains("unknown_user"));

        // The forge vector: NO AuthEndpoints line may carry a raw CR or LF from the username.
        // RED on the un-sanitized version (the value's \r\n reaches the rendered message).
        Assert.All(authWarnings, m =>
        {
            Assert.DoesNotContain("\r", m);
            Assert.DoesNotContain("\n", m);
        });

        // The value is still recorded, just flattened to one safe line ("attacker" + "[FORGED]"
        // survive; only the control chars between them are stripped).
        Assert.Contains(authWarnings, m => m.Contains("attacker") && m.Contains("[FORGED]"));

        AssertNoSecretLogged(factory.Provider, password);
    }

    // ─────────────────────────────── assertions / helpers ───────────────────────────────

    private static List<string> AuthWarnings(CapturingLoggerProvider provider)
        => provider.Records
            .Where(r => r.Category == AuthEndpointsCategory && r.Level == LogLevel.Warning)
            .Select(r => r.Message)
            .ToList();

    private static void AssertNoSecretLogged(CapturingLoggerProvider provider, params string[] secrets)
    {
        var all = provider.Records.Select(r => r.Message).ToList();
        foreach (var secret in secrets)
            Assert.DoesNotContain(all, m => m.Contains(secret));
    }

    // ─────────────────────────────── the capturing host ───────────────────────────────

    /// <summary>
    /// Boots the real <c>StatsTid.Backend.Api</c> with (a) the per-test container connection
    /// string, (b) the requested <c>Auth:UseDatabase</c> mode, and (c) a capturing
    /// <see cref="ILoggerProvider"/> — all injected at HOST configuration so they are observed
    /// before <c>Program.cs</c> reads them / before the <c>ILoggerFactory</c> is built.
    /// </summary>
    private sealed class CapturingWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly bool _useDbAuth;

        public CapturingLoggerProvider Provider { get; } = new();

        public CapturingWebApplicationFactory(string connectionString, bool useDbAuth)
        {
            _connectionString = connectionString;
            _useDbAuth = useDbAuth;
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EventStore"] = _connectionString,
                    ["Auth:UseDatabase"] = _useDbAuth ? "true" : "false",
                }));
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(Provider);
                logging.SetMinimumLevel(LogLevel.Information); // Warning passes; guards against a low appsettings floor
            });
            return base.CreateHost(builder);
        }
    }

    /// <summary>A minimal thread-safe in-memory <see cref="ILoggerProvider"/> that records the
    /// category, level, and FORMATTED message of every log call (the formatted message is what
    /// substitutes the structured placeholders, so it carries the actual username/reason values).</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLog> Records { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Records);

        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentQueue<CapturedLog> _records;

            public CapturingLogger(string category, ConcurrentQueue<CapturedLog> records)
            {
                _category = category;
                _records = records;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _records.Enqueue(new CapturedLog(_category, logLevel, formatter(state, exception)));
        }
    }

    private sealed record CapturedLog(string Category, LogLevel Level, string Message);
}
