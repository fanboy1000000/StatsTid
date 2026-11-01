using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S147 / TASK-14710-T (AC-230-4; QUAL-230 T5a point 2; owner ruling OQ-2) — a leaver cannot log in from the
/// day after their last working day, even though the automatic switch-off now waits until day 6.
///
/// <para><b>Plain language.</b> Mette's last working day is D. On D she can still log in. From D+1 her password
/// no longer gets her in: the system answers exactly what it answers for a wrong password (a plain 401, no
/// date, no hint), and only the server's own log says why (<c>employment_ended</c>). Someone with no end date
/// logs in as always. This matters because, since the switch-off moved to day 6, Mette is still an ACTIVE
/// user on D+1..D+5, and the old "inactive users cannot log in" filter no longer closes the door by itself.</para>
///
/// <para><b>Seeding and host.</b> The users are seeded directly: <c>is_active = TRUE</c> with the end date
/// set (the real grace state), a known BCrypt password, database auth (the default). The host's clock is
/// pinned with <see cref="FixedTimeProvider"/> to D+5 and its leaver job is REMOVED
/// (<c>withoutLeaverJob: true</c>), so on today's code the seeded user stays active and the old filter lets
/// the login through — which is what makes the fact F. Docker — CI-pending.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LeaverLoginTests : IAsyncLifetime
{
    private const string LoginPath = "/api/auth/login";
    private const string AuthEndpointsCategory = "StatsTid.Backend.Api.Endpoints.AuthEndpoints";
    private const string Password = "S147!LeaverLogin1";

    /// <summary>The last day employed (D): Friday 2026-03-13. The host runs on D+5 (Wednesday 2026-03-18).</summary>
    private static readonly DateOnly Today = new(2026, 3, 18);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program>? _host;
    private readonly CapturingLoggerProvider _logs = new();

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        // No leaver job: a seeded active subject with a past end date must stay active for the fact to be F.
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString, withoutLeaverJob: true);
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// AC-230-4. Kind F. On D+5 (a host pinned there, active users, database auth): a user whose last day was 1, 2,
    /// 3, 4 or 5 days back gets 401 with the SAME generic body as a wrong password, and the server log names the
    /// class <c>employment_ended</c> (never the password, never a date); a user whose last day is TODAY (the
    /// boundary: D itself still logs in), a user with a future end date and a user with NO end date each log in
    /// (200, a token). Red conditions: <c>Assert.Equal(Unauthorized)</c> for each of the five departed users —
    /// today the login filters only <c>is_active</c>, so all five answer 200; the three that log in are boundary
    /// pins (hold today) that stop the new check from over-reaching.
    /// </summary>
    [Fact]
    public async Task EndDateBeforeToday_Login401_EmploymentEnded_LastDayAndNullStillLogIn()
    {
        var departed = new List<(string User, int DaysBack)>();
        for (var back = 1; back <= 5; back++)
            departed.Add((await SeedUserAsync("ll_gone_d" + back, Today.AddDays(-back)), back));
        var lastDay = await SeedUserAsync("ll_lastday", Today);          // D itself: still employed
        var future = await SeedUserAsync("ll_future", Today.AddDays(30));
        var openEnded = await SeedUserAsync("ll_null", end: null);
        var wrongPw = departed[0].User;

        _host = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Today)));
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(_logs);
                logging.SetMinimumLevel(LogLevel.Information);
            });
        });
        using var client = _host.CreateClient(); // login is anonymous

        // The reference refusal: a WRONG password for a departed user's name answers the generic 401.
        using var wrong = await client.PostAsJsonAsync(LoginPath, new { username = wrongPw, password = "not-the-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        var wrongBody = await wrong.Content.ReadAsStringAsync();
        var wrongType = wrong.Content.Headers.ContentType?.ToString();

        foreach (var (user, back) in departed)
        {
            using var rsp = await client.PostAsJsonAsync(LoginPath, new { username = user, password = Password });
            var body = await rsp.Content.ReadAsStringAsync();
            Assert.True(rsp.StatusCode == HttpStatusCode.Unauthorized,
                $"a leaver whose last day was {back} day(s) ago must be refused at login, got {(int)rsp.StatusCode}: {body}");
            // The SAME generic answer as a wrong password: no enumeration oracle, no date, no hint.
            Assert.Equal(wrongBody, body);
            Assert.Equal(wrongType, rsp.Content.Headers.ContentType?.ToString());
            Assert.DoesNotContain("2026", body);
            Assert.DoesNotContain("employment", body, StringComparison.OrdinalIgnoreCase);

            Assert.Contains(AuthWarnings(), m =>
                m.Contains(user) && m.Contains("employment_ended"));
        }
        // The password never reaches a log line.
        Assert.DoesNotContain(_logs.Records.Select(r => r.Message), m => m.Contains(Password));

        foreach (var ok in new[] { lastDay, future, openEnded })
        {
            using var rsp = await client.PostAsJsonAsync(LoginPath, new { username = ok, password = Password });
            var raw = await rsp.Content.ReadAsStringAsync();
            Assert.True(rsp.StatusCode == HttpStatusCode.OK,
                $"{ok} (no departed end date) must log in, got {(int)rsp.StatusCode}: {raw}");
            Assert.False(string.IsNullOrEmpty(JsonDocument.Parse(raw).RootElement.GetProperty("token").GetString()));
        }
        // None of the three successful logins produced an employment_ended line.
        Assert.DoesNotContain(AuthWarnings(), m =>
            (m.Contains(lastDay) || m.Contains(future) || m.Contains(openEnded)) && m.Contains("employment_ended"));
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private List<string> AuthWarnings()
        => _logs.Records
            .Where(r => r.Category == AuthEndpointsCategory && r.Level == LogLevel.Warning)
            .Select(r => r.Message)
            .ToList();

    /// <summary>Seeds an ACTIVE user with a known BCrypt password and the given end date (the grace state).</summary>
    private async Task<string> SeedUserAsync(string prefix, DateOnly? end)
    {
        var id = $"{prefix}_{Guid.NewGuid().ToString("N")[..6]}";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, id, "STY01", "AC", "OK24");
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET password_hash = @hash, employment_end_date = @e WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("hash", BCrypt.Net.BCrypt.HashPassword(Password));
        cmd.Parameters.AddWithValue("e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        return id;
    }

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
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _records.Enqueue(new CapturedLog(_category, logLevel, formatter(state, exception)));
        }
    }

    private sealed record CapturedLog(string Category, LogLevel Level, string Message);
}
