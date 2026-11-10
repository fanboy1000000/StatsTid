using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Loading;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.PayPin;

/// <summary>
/// S148 / TASK-14800 (PIN-1) — builds one demo world end to end and returns the canonical text of the pay it produced.
///
/// <para>
/// <b>The path</b> (one Postgres database per world, because the generator's user ids are positional and its SQL inserts
/// with <c>ON CONFLICT DO NOTHING</c>, so a second world loaded into the first one's database would silently keep the
/// first world's users): (1) init.sql; (2) the world's generated structural SQL; (3) the in-process Backend host boots,
/// so its start-up seeders backfill the profile rows of the users that now exist, the order the composed stack runs in;
/// (4) the REAL <see cref="DemoLoader"/> loads the world through that host, its <see cref="ApiClient"/> built over the
/// host's handler (two signers, as the loader does them); (5) the in-process Payroll host, with the real Rule Engine
/// behind it, runs <c>/calculate-and-export</c> once for every month the loader approved; (6) the text is built.
/// </para>
///
/// <para>
/// <b>The clocks.</b> The Backend and Payroll hosts' <see cref="TimeProvider"/> is pinned to the world's reference date,
/// the day the demo is built for, so no host-side reading of "today" moves the text with the calendar. One reading is
/// not pinnable from here: <c>DemoLoader.SetProfilesAsync</c> stamps the part-time/position profile edits effective from
/// the machine's Copenhagen day (<c>TimeProvider.System</c>), which is always after both activity months, so the dated
/// profile payroll resolves for those months is the seeded one on any day this runs. JWT lifetimes read the system clock
/// and are not part of the output.
/// </para>
///
/// <para>Docker-gated by its users: CI-pending, never locally green.</para>
/// </summary>
internal static class DemoWorldPayRun
{
    private const string BaseUrl = "http://localhost";

    /// <summary>Runs the world. Fails (with the employee-month and what happened) before returning a text when a
    /// non-vacuity rule is broken; the text of a world that fails one is never produced.</summary>
    internal static async Task<string> RunAsync(PinnedWorld world)
    {
        var dataset = new DemoGenerator(PinnedWorld.Scale, world.Seed, world.ReferenceDate).Generate();
        var manifest = dataset.Manifest;
        var approved = manifest.Activity
            .Where(a => a.PeriodOutcome == "APPROVED")
            .OrderBy(a => a.EmployeeId, StringComparer.Ordinal)
            .ToList();

        // (c) the world holds at least three APPROVED months.
        if (approved.Count < PinnedWorld.MinApprovedMonths)
            Assert.Fail($"{world.File}: the world holds {approved.Count} APPROVED months at seed {world.Seed}; " +
                        $"the pin needs at least {PinnedWorld.MinApprovedMonths}.");

        await using var container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await container.StartAsync();
        var connectionString = container.GetConnectionString();

        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(connectionString);
        await ApplySqlAsync(connectionString, SqlEmitter.Emit(dataset));

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        DemoLoader.LoadResult load;
        await using (var backendBase = new StatsTidWebApplicationFactory(connectionString, withoutLeaverJob: true))
        await using (var backend = backendBase.WithFixedToday(world.ReferenceDate).WithWebHostBuilder(builder =>
                         builder.ConfigureTestServices(services =>
                             services.AddSingleton<IHttpClientFactory>(new RuleEngineStubFactory()))))
        {
            using (backend.CreateClient())
            {
                // The first CreateClient boots the host (its start-up seeders run here, over the world's users).
            }

            using var api = new ApiClient(BaseUrl, backend.Server.CreateHandler());
            load = await new DemoLoader(api, manifest, 200, _ => { }).LoadAsync(cts.Token);
        }

        // (e) the loader approved what the generator planned, and no month missed its outcome (FAIL-006).
        if (load.PeriodsApproved != approved.Count || load.PeriodOutcomeFailures != 0)
            Assert.Fail($"{world.File}: the loader approved {load.PeriodsApproved} months and reports " +
                        $"{load.PeriodOutcomeFailures} outcome failures; the manifest plans {approved.Count} APPROVED. " +
                        $"Warnings: {string.Join(" | ", load.Warnings.Take(10))}");

        var problems = new List<string>();
        var lines = new List<PayrollExportLine>();
        var calculated = 0;

        await using (var payrollBase = new PayrollHostFactory(connectionString, new RuleRegistry().GetAll(), useRealRuleEngine: true))
        await using (var payroll = payrollBase.WithWebHostBuilder(builder =>
                         builder.ConfigureTestServices(services =>
                             services.AddSingleton<TimeProvider>(new FixedTimeProvider(world.ReferenceDate)))))
        {
            using var client = payroll.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", FlexLedgerHostFixture.MintGlobalAdminToken());

            foreach (var month in approved)
            {
                var start = new DateOnly(month.Year, month.Month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                var body = new Dictionary<string, object?>
                {
                    ["profile"] = new EmploymentProfile
                    {
                        EmployeeId = month.EmployeeId,
                        AgreementCode = month.AgreementCode,
                        OkVersion = month.OkVersion,
                        EmploymentCategory = "Standard",
                        PartTimeFraction = 1.0m,
                        OrgId = month.OrgId,
                    },
                    ["entries"] = Array.Empty<TimeEntry>(),
                    ["absences"] = Array.Empty<AbsenceEntry>(),
                    ["periodStart"] = start,
                    ["periodEnd"] = end,
                };

                var response = await client.PostAsJsonAsync("/api/payroll/calculate-and-export", body, cts.Token);
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var label = $"{month.EmployeeId} {start:yyyy-MM}";

                // (a) any APPROVED month refused.
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    problems.Add($"{label}: refused with {(int)response.StatusCode} (kind '{Kind(json)}'): {json}");
                    continue;
                }

                var result = FlexLedgerHostFixture.ReadResult(json);
                if (!result.Success)
                {
                    problems.Add($"{label}: answered 200 but not successful: {result.ErrorMessage}");
                    continue;
                }

                calculated++;

                // (d) an APPROVED month yields at least one line.
                if (result.ExportLines.Count == 0)
                    problems.Add($"{label}: calculated and exported with no pay line");
                lines.AddRange(result.ExportLines);
            }
        }

        // (b) the number calculated is not below the loader's.
        if (calculated < load.PeriodsApproved)
            problems.Add($"calculated {calculated} months, below the loader's {load.PeriodsApproved} approved");

        var records = await ReadRecordsAsync(connectionString);
        if (records.Count != calculated)
            problems.Add($"payroll_export_records holds {records.Count} rows for {calculated} calculated months");

        if (problems.Count > 0)
            Assert.Fail($"{world.File}: non-vacuity broken:\n" + string.Join("\n", problems));

        var activity = approved[0];
        var header = PayLineCanonicaliser.Header(
            PinnedWorld.Scale, world.ReferenceDate, activity.Year, activity.Month, world.Seed,
            approved.Count, load.PeriodsApproved, calculated, lines.Count);
        return PayLineCanonicaliser.Canonicalise(header, lines, records);
    }

    /// <summary>Golden present: compare, failing on the first differing line. Golden absent: fail with the capture.
    /// Never writes a file.</summary>
    internal static void AssertEqualsGolden(PinnedWorld world, string text)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PayPin", "Golden", world.File);
        if (!File.Exists(path))
        {
            Assert.Fail(
                $"golden capture: {world.File} is absent. Commit the text between the markers, unchanged, as " +
                $"tests/StatsTid.Tests.Regression/PayPin/Golden/{world.File}.\n" +
                $"=====BEGIN GOLDEN {world.File}=====\n{text}=====END GOLDEN {world.File}=====");
        }

        // The owner's checkout converts line endings; the canonical text is LF.
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        if (string.Equals(expected, text, StringComparison.Ordinal))
            return;

        var expectedLines = expected.Split('\n');
        var actualLines = text.Split('\n');
        var i = 0;
        while (i < expectedLines.Length && i < actualLines.Length && expectedLines[i] == actualLines[i])
            i++;
        Assert.Fail(
            $"{world.File}: the pay text differs from the golden, first at line {i + 1}.\n" +
            $"expected: {(i < expectedLines.Length ? expectedLines[i] : "<end of golden>")}\n" +
            $"actual:   {(i < actualLines.Length ? actualLines[i] : "<end of text>")}\n" +
            $"=====BEGIN ACTUAL {world.File}=====\n{text}=====END ACTUAL {world.File}=====");
    }

    private static async Task ApplySqlAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): the SQL is the demo generator's own output for a fixed seed, run on a
        // throwaway test database; no user input reaches it.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<List<PayRecord>> ReadRecordsAsync(string connectionString)
    {
        var records = new List<PayRecord>();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT employee_id, year, month, opening_balance, closing_balance, balance_provenance FROM payroll_export_records",
            conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            records.Add(new PayRecord(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return records;
    }

    private static string Kind(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var name in new[] { "kind", "error" })
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(name, out var value))
                    return value.ToString();
        }
        catch (JsonException)
        {
            // not JSON: the body is in the message
        }
        return "?";
    }

    // The Backend's Skema save calls the Rule Engine's validate-entitlement over HTTP; this answers it with the real
    // EntitlementValidationRule, as the Skema-to-payroll scenario does (SickCodeVocabularyPayrollTests).
    private sealed class RuleEngineStubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new RuleEngineStubHandler(), disposeHandler: false)
        {
            BaseAddress = new Uri("http://rule-engine:8080"),
        };
    }

    private sealed class RuleEngineStubHandler : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/api/rules/validate-entitlement", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            var req = JsonSerializer.Deserialize<ValidateEntitlementRequest>(json, Camel)!;
            var result = EntitlementValidationRule.Evaluate(req);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(result, Camel), Encoding.UTF8, "application/json"),
            };
        }
    }
}
