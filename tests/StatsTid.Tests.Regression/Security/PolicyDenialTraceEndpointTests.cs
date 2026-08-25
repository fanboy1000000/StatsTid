using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// QUAL-009 / SEC-038 — end-to-end proof of the policy-denial trace, booting the REAL
/// <c>StatsTid.Backend.Api</c> against a real Postgres (Testcontainers). It proves the WIRING the
/// unit tests cannot: that the custom <see cref="DenialLoggingAuthorizationResultHandler"/> is
/// actually registered (replacing the framework default) and that the before-authz audit-row
/// middleware is positioned so a real authorization short-circuit still produces a row.
///
/// <para><b>The gap (baseline):</b> an authorization denial short-circuits the pipeline BEFORE the
/// audit middleware runs, so a 403/401 produced neither an application log nor an <c>audit_log</c>
/// row — the cause of a 403 could not be reconstructed. The fix logs EVERY denial and writes an
/// <c>audit_log</c> row on ADMIN-STRICT/MUTATING routes only.</para>
///
/// <para><b>Two cases, correlation-filtered</b> (each request carries a unique
/// <c>X-Correlation-Id</c> the middleware adopts, so the DB assertions are immune to background
/// noise):
/// <list type="bullet">
///   <item><description><b>Admin/mutating denial → ROW.</b> An Employee-role token POSTing to the
///   <c>LocalAdminOrAbove</c> endpoint <c>/api/admin/roles/grant</c> gets 403 and writes exactly one
///   <c>audit_log</c> failure row (http_status 403), plus a <c>Warning</c> log. RED on baseline: no
///   row, no log.</description></item>
///   <item><description><b>Routine read denial → NO ROW (log only).</b> An anonymous GET to the
///   <c>EmployeeOrAbove</c> read endpoint <c>/api/skema/{employeeId}/month</c> gets a 401 challenge,
///   writes NO row, and logs at <c>Information</c>. This is the level discipline + no-false-positive
///   guarantee.</description></item>
/// </list></para>
///
/// <para><b>Docker:</b> reads/writes a real <c>audit_log</c>, so the whole class is Docker-gated
/// (CI-only).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PolicyDenialTraceEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string HandlerCategory = "StatsTid.Auth.DenialLoggingAuthorizationResultHandler";

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
    //  Admin/mutating denial → an audit_log failure ROW + a Warning log.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AdminMutatingDenial_WritesExactlyOneFailureRow_AndWarns()
    {
        await using var factory = new CapturingBackendFactory(_harness.ConnectionString);
        using var client = factory.CreateClient();
        var correlationId = Guid.NewGuid();

        // An Employee token has no LocalAdmin/GlobalAdmin role, so LocalAdminOrAbove denies it (403)
        // at the policy layer, BEFORE the handler runs.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/roles/grant")
        {
            Content = JsonContent.Create(new
            {
                userId = "qual009_target",
                roleId = "LOCAL_LEADER",
                orgId = "STY01",
                scopeType = "ORG_ONLY",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", EmployeeToken());
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        // Exactly one audit_log failure row for THIS request (correlation-filtered).
        var rows = await ReadAuditRowsAsync(correlationId);
        var row = Assert.Single(rows);
        Assert.Equal("failure", row.Result);
        Assert.Equal(403, row.HttpStatus);
        Assert.Equal("POST", row.HttpMethod);
        Assert.Equal("/api/admin/roles/grant", row.HttpPath);

        // Admin/mutating denial → Warning-level structured log.
        Assert.Contains(factory.Provider.Records, r =>
            r.Category == HandlerCategory && r.Level == LogLevel.Warning && r.Message.Contains("Authorization denied"));
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Routine read denial → NO row (log only), Information level.
    // ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RoutineReadDenial_WritesNoRow_LogsInformationOnly()
    {
        await using var factory = new CapturingBackendFactory(_harness.ConnectionString);
        using var client = factory.CreateClient(); // no bearer token → an anonymous read
        var correlationId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/skema/qual009_emp/month?year=2026&month=1");
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());

        using var resp = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        // Log-only: NO audit_log row for a routine read-scope denial.
        var rows = await ReadAuditRowsAsync(correlationId);
        Assert.Empty(rows);

        // Level discipline: routine read → Information (not Warning).
        Assert.Contains(factory.Provider.Records, r =>
            r.Category == HandlerCategory && r.Level == LogLevel.Information && r.Message.Contains("Authorization denied"));
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private static string EmployeeToken()
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var scopes = new[] { new RoleScope(StatsTidRoles.Employee, "STY01", "ORG_ONLY") };
        return tokenService.GenerateToken(
            employeeId: "qual009_employee", name: "qual009_employee", role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: "STY01", scopes: scopes);
    }

    private sealed record AuditRow(string Result, int? HttpStatus, string? HttpMethod, string? HttpPath);

    private async Task<List<AuditRow>> ReadAuditRowsAsync(Guid correlationId)
    {
        var rows = new List<AuditRow>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT result, http_status, http_method, http_path FROM audit_log WHERE correlation_id = @c", conn);
        cmd.Parameters.AddWithValue("c", correlationId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return rows;
    }

    // ─────────────────────────────── the capturing host ───────────────────────────────

    private sealed class CapturingBackendFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        public CapturingLoggerProvider Provider { get; } = new();

        public CapturingBackendFactory(string connectionString) => _connectionString = connectionString;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EventStore"] = _connectionString,
                }));
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(Provider);
                logging.SetMinimumLevel(LogLevel.Information);
            });
            return base.CreateHost(builder);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<Captured> Records { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Records);
        public void Dispose() { }

        private sealed class Logger : ILogger
        {
            private readonly string _cat;
            private readonly ConcurrentQueue<Captured> _sink;
            public Logger(string cat, ConcurrentQueue<Captured> sink) { _cat = cat; _sink = sink; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
                Func<TState, Exception?, string> formatter)
                => _sink.Enqueue(new Captured(_cat, level, formatter(state, ex)));
        }
    }

    private sealed record Captured(string Category, LogLevel Level, string Message);
}
