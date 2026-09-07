using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Tests.Regression.Outbox;

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

    // ─── S133 / TASK-13307 (QUAL-016) atomic-outbox rollback harness ─────────────────────
    // The ONE reusable way to drive an atomic-outbox rollback through the REAL wire: derive a
    // host identical to this one EXCEPT its single IOutboxEnqueue registration is swapped for a
    // throwing double. The endpoint's in-tx `outbox.Enqueue…` call then throws, and — because
    // the throw happens BEFORE `tx.CommitAsync` — PostgreSQL rolls back the WHOLE transaction on
    // dispose. A converted `*AtomicTests` posts to the endpoint, expects the 5xx the escaped
    // throw produces, and reuses ForcedRollbackHarness.AssertNo*Async to pin that no state /
    // audit / canonical-event / outbox row leaked.
    //
    // This HOISTS the local `ThrowingOutboxFactory()` helper proven in S127's
    // Approval.SendAtomicityTests into the shared factory so every QUAL-016 conversion reuses
    // ONE mechanism instead of re-declaring it per file. Only IOutboxEnqueue is swapped — the
    // IEventStore / PostgresEventStore registration the OutboxPublisher depends on is untouched,
    // so ONLY the state-change-site enqueue faults, never the background drain.
    //
    // The derived host is disposable and independent: disposing it stops only that host; this
    // factory's own host + the shared testcontainer are unaffected. Boot the returned factory's
    // client only AFTER the per-test employee/config is seeded — a fresh derived host re-runs the
    // startup seeders, and a seeder that had to backfill a row would invoke the throwing outbox at
    // startup (the S63/S65 boot-order lesson SendAtomicityTests documents).

    /// <summary>
    /// A derived host whose <see cref="IOutboxEnqueue"/> throws on EVERY enqueue
    /// (<see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/>) — for the single-emit endpoints
    /// (create/update/publish-no-supersede/archive/approve/reject/reopen/time/absence/…), whose
    /// FIRST and only enqueue must roll the whole state-change tx back.
    /// </summary>
    public WebApplicationFactory<Program> WithThrowingOutbox()
        => WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOutboxEnqueue>();
                services.AddSingleton<IOutboxEnqueue>(new ForcedRollbackHarness.ThrowingOutboxEnqueue());
            }));

    /// <summary>
    /// A derived host whose <see cref="IOutboxEnqueue"/> DELEGATES the first enqueue to the real
    /// <see cref="PostgresEventStore"/> (so the first outbox row genuinely lands in-tx) and throws
    /// on the second and later calls (<see cref="ForcedRollbackHarness.ThrowOnSecondCallOutboxEnqueue"/>).
    /// This is the DUAL-EMIT case — the agreement-config publish that supersedes a prior ACTIVE
    /// emits PUBLISHED (enqueue #1) then ARCHIVED (enqueue #2); a fault on #2 must roll back the
    /// whole tx INCLUDING the successfully-inserted #1 row. The real <see cref="PostgresEventStore"/>
    /// is still registered after the <see cref="IOutboxEnqueue"/> swap, so the decorator resolves it
    /// as its inner from the SAME derived-host container.
    /// </summary>
    public WebApplicationFactory<Program> WithThrowOnSecondEnqueueOutbox()
        => WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOutboxEnqueue>();
                services.AddSingleton<IOutboxEnqueue>(sp =>
                    new ForcedRollbackHarness.ThrowOnSecondCallOutboxEnqueue(
                        sp.GetRequiredService<PostgresEventStore>()));
            }));

    // ─── S139 / TASK-13906 (PAT-008) fixed-clock harness ─────────────────────────────────
    // Program.cs:395 registers `TimeProvider.System` as the production default. This derived
    // host replaces it with a `FixedTimeProvider` so every endpoint that has been converted onto
    // the seam (rather than reading the wall clock's UtcNow directly) derives "today" from the pinned
    // date instead of the real wall clock — the fix for S138's two lost CI runs, where a
    // "today minus N days" pin silently proved nothing on the two-in-seven days that offset
    // landed on a weekend (a zero-norm day the product rejects and revaluation skips).

    /// <summary>
    /// A derived host whose <see cref="TimeProvider"/> is pinned to UTC midnight of
    /// <paramref name="today"/> (via <see cref="FixedTimeProvider"/>), REPLACING the production
    /// <c>TimeProvider.System</c> singleton Program.cs:395 registers. Opt-in per test, mirroring
    /// <see cref="WithThrowingOutbox"/> — the seam is per-endpoint (only endpoints explicitly
    /// converted onto <c>TimeProvider</c> read this; an unconverted endpoint still reading the wall
    /// clock's UtcNow instant directly will silently ignore the override, per PAT-008's Agent
    /// Guidance).
    ///
    /// <para>
    /// <b>Boot-order rule — READ BEFORE SEEDING A FIXTURE AGAINST THIS HOST.</b> Like every
    /// <c>WithWebHostBuilder</c>-derived host, calling
    /// <see cref="WebApplicationFactory{TEntryPoint}.CreateClient"/> on the factory THIS method
    /// returns RE-RUNS <c>Program.cs</c>'s startup seeders (see the note above
    /// <see cref="WithThrowingOutbox"/> at :171-175 — the same lesson applies here) against the
    /// SAME Postgres container. Any "absent-state" fixture the test needs — a profile-less
    /// employee, a missing eligibility row, or any direct-INSERT the test seeds by hand — MUST be
    /// created AFTER that first <c>CreateClient()</c> call on THIS derived host, never before and
    /// never only on a different host's boot, or the very seeder that (re)populates the "missing"
    /// row erases the absence the test relies on. A fixture employee's hire date
    /// (<c>employment_start_date</c>) must also be ON OR BEFORE <paramref name="today"/>:
    /// <c>EmployeeProfileRepository</c> refuses any <c>EffectiveFrom &lt; employment_start_date</c>,
    /// and <paramref name="today"/> is exactly what a converted future-dating guard compares
    /// against once it reads this seam instead of the wall clock.
    /// </para>
    /// </summary>
    public WebApplicationFactory<Program> WithFixedToday(DateOnly today)
        => WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(today))));

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
