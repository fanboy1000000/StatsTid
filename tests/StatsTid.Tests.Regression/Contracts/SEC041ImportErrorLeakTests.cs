using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// SEC-041 (← QUAL-049) — the bulk reporting-line import's catch-all for an UNEXPECTED failure must
/// NOT (a) echo the raw exception text back to the caller (an information leak — internals/DB text can
/// ride the message) nor (b) swallow the error silently server-side (an observability/audit blind spot).
///
/// <para><b>Plain-language:</b> the import endpoint has a last-resort <c>catch (Exception)</c> for
/// anything not already handled by its specific domain/concurrency catches. Before the fix that
/// catch returned <c>{ "error": "Import failed", "detail": ex.Message }</c> and logged nothing — so the
/// raw internal message reached the client and the failure vanished from the server logs. The fix drops
/// the raw <c>detail</c> (generic body only) AND logs the full exception + operation context at Error.</para>
///
/// <para><b>How the catch-all is forced deterministically:</b> a decorator over <see cref="IOutboxEnqueue"/>
/// is injected via <c>ConfigureTestServices</c> (last-registration-wins) that throws a distinctive
/// <see cref="ImportFaultInjectionException"/> ONLY for the import's own stream key
/// (<c>reporting-line-import-*</c>) and delegates every OTHER enqueue (the boot seeders' events) to the
/// real <see cref="PostgresEventStore"/> so host boot is unaffected. The enqueue is the last step inside
/// the import's write transaction (right before commit), so a valid single-row import runs the whole
/// happy path, then the injected throw rolls the tx back and lands in the catch-all — the exact code
/// under test. The thrown message is a RAW SENTINEL; the assertions prove it never reaches the client.</para>
///
/// <para><b>RED-before-green:</b> on the pre-fix source the response body carried the sentinel (the
/// <c>DoesNotContain</c> assertion fails) AND no server-side Error log was emitted (the log assertion
/// fails). Both flip green after the fix. Docker-backed (the established harness); CI-deferred where no
/// Docker daemon is present. Natural keys are <c>SEC041*</c>, disjoint from the boot seeders and every
/// other suite.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SEC041ImportErrorLeakTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // The raw text the injected fault carries. The whole point of the fix is that this string, which
    // stands in for any internal/DB exception message, must NEVER appear in the client-facing body.
    private const string RawSentinel = "SEC041_RAW_EXCEPTION_LEAK_SENTINEL_2f9c1a_do_not_surface";

    private const string Mao = "SEC041M";           // MAO org (JWT org + tree parent)
    private const string ImportOrg = "SEC041O";      // the import's declared tree root
    private const string EmployeeId = "sec041_emp";
    private const string ManagerId = "sec041_mgr";
    private const string ActorId = "sec041_gadmin";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private readonly ListLoggerProvider _logs = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);

        // Derived host: fault-inject the outbox for the import stream + capture server-side logs.
        _client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                // Last registration wins for a single-service resolve → the import handler (and the
                // boot seeders) resolve THIS decorator. It delegates every non-import enqueue to the
                // real event store, so host boot succeeds normally.
                services.AddSingleton<IOutboxEnqueue>(sp =>
                    new ImportFailingOutbox(sp.GetRequiredService<PostgresEventStore>()));
            });
            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
        }).CreateClient();

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await SeedAsync(conn);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public async Task Import_UnexpectedException_DoesNotLeakRawMessage_AndLogsServerSide()
    {
        _logs.Clear(); // drop boot noise so the snapshot reflects only the request under test

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reporting-lines/import")
        {
            Content = new StringContent(
                $$"""
                { "organisationId": "{{ImportOrg}}",
                  "rows": [ { "employeeId": "{{EmployeeId}}", "managerId": "{{ManagerId}}", "effectiveFrom": "2026-01-01" } ] }
                """,
                Encoding.UTF8, "application/json"),
        };

        using var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Precondition: the injected fault really drove the handler into the catch-all → a 500.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // (1) INFORMATION LEAK — the raw exception message must NOT appear in the client body.
        //     (RED on the pre-fix source, which returned `detail = ex.Message`.)
        Assert.DoesNotContain(RawSentinel, body, StringComparison.Ordinal);
        // The client still gets the generic, contract-shaped problem body (non-vacuous).
        Assert.Contains("Import failed", body, StringComparison.Ordinal);

        // (2) OBSERVABILITY / AUDIT — a server-side Error log carrying the actual exception WAS emitted.
        //     (RED on the pre-fix source, whose catch-all logged nothing.)
        var records = _logs.Snapshot();
        Assert.Contains(records, r =>
            r.Level == LogLevel.Error && r.Exception is ImportFaultInjectionException);
    }

    private static string MintGlobalAdminToken()
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: ActorId,
            name: ActorId,
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            orgId: Mao,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
    }

    // ── Fixture seed — one MAO + one Organisation + an active employee/manager pair in that org, so
    //    the import's whole pre-validation + write pass runs and only the injected outbox throw fails. ──
    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
                ('SEC041M', 'SEC041 Ministerie', 'MAO',          NULL,       '/SEC041M/',          'HK', 'OK24'),
                ('SEC041O', 'SEC041 Import',     'ORGANISATION', 'SEC041M',  '/SEC041M/SEC041O/',  'HK', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, is_active) VALUES
                ('sec041_emp', 'sec041_emp', '$2a$11$fake', 'SEC041 Medarbejder', 'sec041_emp@test.dk', 'SEC041O', 'HK', 'OK24', TRUE),
                ('sec041_mgr', 'sec041_mgr', '$2a$11$fake', 'SEC041 Leder',       'sec041_mgr@test.dk', 'SEC041O', 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Fault-injection outbox decorator — throws ONLY for the import stream key.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>A distinctive exception type NOT matched by any of the import handler's specific
    /// catches (it derives straight from <see cref="Exception"/>, so it is neither an
    /// <c>InvalidOperationException</c> nor any of the domain/concurrency types) — it is guaranteed to
    /// land in the last-resort <c>catch (Exception)</c> under test.</summary>
    private sealed class ImportFaultInjectionException : Exception
    {
        public ImportFaultInjectionException(string message) : base(message) { }
    }

    private sealed class ImportFailingOutbox : IOutboxEnqueue
    {
        private readonly IOutboxEnqueue _inner;
        public ImportFailingOutbox(IOutboxEnqueue inner) => _inner = inner;

        public Task EnqueueAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, IDomainEvent @event, CancellationToken ct = default)
        {
            if (streamId.StartsWith("reporting-line-import-", StringComparison.Ordinal))
                throw new ImportFaultInjectionException(RawSentinel);
            return _inner.EnqueueAsync(conn, tx, streamId, @event, ct);
        }

        public Task<long> EnqueueAndReturnIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, IDomainEvent @event, CancellationToken ct = default)
        {
            if (streamId.StartsWith("reporting-line-import-", StringComparison.Ordinal))
                throw new ImportFaultInjectionException(RawSentinel);
            return _inner.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  In-memory log capture (records category / level / message / exception).
    // ════════════════════════════════════════════════════════════════════════════════

    private sealed record CapturedLog(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly List<CapturedLog> _records = new();
        private readonly object _gate = new();

        public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, this);
        public void Dispose() { }

        private void Record(CapturedLog record)
        {
            lock (_gate) _records.Add(record);
        }

        public void Clear()
        {
            lock (_gate) _records.Clear();
        }

        public IReadOnlyList<CapturedLog> Snapshot()
        {
            lock (_gate) return _records.ToArray();
        }

        private sealed class ListLogger : ILogger
        {
            private readonly string _category;
            private readonly ListLoggerProvider _owner;
            public ListLogger(string category, ListLoggerProvider owner) => (_category, _owner) = (category, owner);

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _owner.Record(new CapturedLog(_category, logLevel, formatter(state, exception), exception));
        }
    }
}
