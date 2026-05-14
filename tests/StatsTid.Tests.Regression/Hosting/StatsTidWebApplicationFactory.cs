using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StatsTid.Infrastructure.Outbox;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S27 / Phase 4c.6 in-process integration harness — boots the real
/// <c>StatsTid.Backend.Api</c> against a per-test Postgres testcontainer using
/// <see cref="WebApplicationFactory{TEntryPoint}"/>. This is the prerequisite for
/// TASK-2710's read-your-write D-tests, which prove the projection layer (not the
/// publisher-drained <c>events</c> table) serves the just-written read while the
/// <see cref="OutboxPublisher"/> background service is stopped.
///
/// <para>
/// Construction shape:
/// <code>
///   await using var harness = await DockerHarness.StartAsync();
///   await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(harness.ConnectionString);
///   await using var factory = new StatsTidWebApplicationFactory(harness.ConnectionString);
///   var client = factory.CreateClient();
///   // ... POST/GET against client ...
///   await factory.StopPublisherAsync();
///   // ... assert read-your-write from projection table ...
/// </code>
/// </para>
///
/// <para>
/// The constructor takes a connection string (typically from
/// <see cref="Segmentation.TestFixtures.DockerHarness.ConnectionString"/>) and
/// overrides the <c>ConnectionStrings:EventStore</c> configuration key in
/// <see cref="ConfigureWebHost"/>. The host environment defaults to
/// <c>Development</c> per <see cref="WebApplicationFactory{TEntryPoint}"/>'s default,
/// which lets <c>JwtValidationSetup</c>'s dev-fallback signing key fire so startup
/// does not require a real JWT key.
/// </para>
///
/// <para>
/// The full <c>docker/postgres/init.sql</c> schema must be applied to the test
/// Postgres BEFORE <see cref="WebApplicationFactory{TEntryPoint}.CreateClient"/> is
/// called, because <c>Program.cs</c> runs the agreement-config and entitlement-config
/// seeders against the configured connection. <see cref="ApplyFullSchemaAsync"/> walks
/// from the test runtime's <see cref="AppContext.BaseDirectory"/> up the directory
/// tree to find the canonical <c>docker/postgres/init.sql</c> and runs it. The
/// existing <c>Segmentation.TestFixtures.DockerHarness.SchemaDdl</c> is a
/// segmentation-suite-only subset and is NOT sufficient for full Backend.Api boot.
/// </para>
/// </summary>
public sealed class StatsTidWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public StatsTidWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Inject the per-test container's connection string into HOST configuration
    /// (which fires BEFORE <see cref="WebApplicationBuilder"/> reads its app
    /// configuration at builder-construction time). This is the only timing point
    /// where the override is observed by <c>Program.cs:11-12</c>'s
    /// <c>builder.Configuration.GetConnectionString("EventStore")</c> read that
    /// captures into the <c>DbConnectionFactory</c> singleton.
    ///
    /// <para>
    /// Per TASK-3001 diagnosis (SPRINT-30): <see cref="ConfigureWebHost"/>'s
    /// <see cref="IWebHostBuilder.ConfigureAppConfiguration"/> fires too late —
    /// the production default <c>127.0.0.1:5432</c> has already been captured.
    /// Host configuration via <see cref="IHostBuilder.ConfigureHostConfiguration"/>
    /// fires earlier and overrides successfully.
    /// </para>
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:EventStore"] = _connectionString,
            }));
        return base.CreateHost(builder);
    }

    /// <summary>
    /// Override <c>ConnectionStrings:EventStore</c> so <c>Program.cs</c> reads our
    /// per-test container's connection string instead of the production default.
    /// All other configuration (JWT signing key dev fallback, etc.) is resolved
    /// from the host's defaults.
    ///
    /// <para>
    /// Retained belt-and-braces alongside the <see cref="CreateHost"/> override
    /// added in TASK-3001b: this path is harmless when the host-configuration
    /// override already won, and keeps any future non-connection-string overrides
    /// intact without re-introducing the timing bug.
    /// </para>
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:EventStore"] = _connectionString,
            });
        });
    }

    /// <summary>
    /// Stops the real <see cref="OutboxPublisher"/> background service so subsequent
    /// reads against event-stream-backed endpoints can prove that a just-written
    /// projection row is visible WITHOUT the publisher having drained the outbox
    /// to canonical events. This is the load-bearing primitive of TASK-2710.
    ///
    /// <para>
    /// VERBATIM mechanism per S27 cycle 3 BLOCKER fix (pinned to prevent
    /// flaky-mechanism drift): resolves the singleton publisher instance through
    /// <see cref="IHostedService"/> DI registration and calls
    /// <see cref="IHostedService.StopAsync"/>. NO <c>Task.Delay</c>, NO config flag,
    /// NO test-double, NO reflection on private fields.
    /// </para>
    /// </summary>
    public async Task StopPublisherAsync()
    {
        var publisher = Services
            .GetServices<IHostedService>()
            .OfType<OutboxPublisher>()
            .Single();
        await publisher.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Companion to <see cref="StopPublisherAsync"/> — re-starts the publisher via
    /// the same <see cref="IHostedService"/> resolution path. Idempotent against
    /// already-running publishers in the sense that <c>BackgroundService.StartAsync</c>
    /// will throw if called twice without a stop in between (callers in TASK-2710
    /// stop-then-start cleanly inside a single test).
    /// </summary>
    public async Task StartPublisherAsync()
    {
        var publisher = Services
            .GetServices<IHostedService>()
            .OfType<OutboxPublisher>()
            .Single();
        await publisher.StartAsync(CancellationToken.None);
    }

    /// <summary>
    /// Applies the canonical <c>docker/postgres/init.sql</c> schema to
    /// <paramref name="connectionString"/>. Walks from
    /// <see cref="AppContext.BaseDirectory"/> up the directory tree to locate the
    /// solution-root <c>docker/postgres/init.sql</c>; the script is idempotent
    /// (<c>CREATE TABLE IF NOT EXISTS</c> throughout) so safe to re-apply across
    /// tests on the same container.
    ///
    /// <para>
    /// Must be called BEFORE <see cref="WebApplicationFactory{TEntryPoint}.CreateClient"/>
    /// because <c>Program.cs</c>'s seeders write against this schema during host
    /// initialization.
    /// </para>
    /// </summary>
    public static async Task ApplyFullSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        var initSqlPath = LocateInitSql();
        var ddl = await File.ReadAllTextAsync(initSqlPath, ct);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string LocateInitSql()
    {
        // Walk up from the test runtime base directory looking for
        // docker/postgres/init.sql. Works regardless of how many levels deep
        // the test bin output sits relative to the solution root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docker", "postgres", "init.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        // Fallback: try relative to the test assembly location (handles edge cases
        // where AppContext.BaseDirectory diverges from the assembly directory).
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (asmDir is not null)
        {
            var d = new DirectoryInfo(asmDir);
            while (d is not null)
            {
                var candidate = Path.Combine(d.FullName, "docker", "postgres", "init.sql");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                d = d.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate docker/postgres/init.sql by walking up from " +
            $"AppContext.BaseDirectory='{AppContext.BaseDirectory}'. " +
            "StatsTidWebApplicationFactory requires the solution-root init.sql to " +
            "apply the full Backend.Api schema before host startup.");
    }
}
