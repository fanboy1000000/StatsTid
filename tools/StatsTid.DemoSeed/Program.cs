// =============================================================================
// S84 / TASK-8401 + TASK-8403 — StatsTid demo-data tool.
//
// Two sub-commands:
//
//   generate --scale {smoke|full} [--out <sql>] [--manifest <json>] [--seed N] [--reference-date YYYY-MM-DD]
//     Emits the deterministic structural SQL (docker/postgres/99-demo-seed.sql) +
//     a JSON manifest. Same (seed, scale, reference-date) ⇒ byte-identical output.
//
//   load --manifest <json> [--base-url http://localhost:5100] [--batch-size 200]
//        [--db-conn "<connstr>"] [--verify]
//     Reads the manifest and drives the LIVE API (event-emitting) to build the
//     reporting trees, grant privileged roles, set part-time profiles, create the
//     activity slice + vikars, and apply the messy-case steps. Idempotent.
//     --verify (or --db-conn alone) runs the post-load tree-invariant + isolation checks.
//
// Exit codes: 0 success · 2 usage error · 3 generation/disjointness failure ·
//             4 load failure · 5 verification failed
// =============================================================================

using System.Text;
using System.Text.Json;
using StatsTid.Tools.DemoSeed.Generation;
using StatsTid.Tools.DemoSeed.Loading;
using StatsTid.Tools.DemoSeed.Model;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

var command = args[0];
var opts = ParseOptions(args.Skip(1).ToArray());

try
{
    return command switch
    {
        "generate" => await RunGenerateAsync(opts),
        "load" => await RunLoadAsync(opts),
        "-h" or "--help" or "help" => UsageExit(),
        _ => Unknown(command),
    };
}
catch (DisjointnessException ex)
{
    Console.Error.WriteLine($"GENERATION FAILED (disjointness): {ex.Message}");
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 4;
}

// ── generate ──
static async Task<int> RunGenerateAsync(Dictionary<string, string> opts)
{
    var scale = opts.GetValueOrDefault("scale", "full");
    var seed = int.TryParse(opts.GetValueOrDefault("seed", "42"), out var s) ? s : 42;
    var referenceDate = opts.TryGetValue("reference-date", out var rd) && DateOnly.TryParse(rd, out var d)
        ? d
        : new DateOnly(2026, 6, 15);

    var repoRoot = FindRepoRoot();
    var outSql = opts.GetValueOrDefault("out", Path.Combine(repoRoot, "docker", "postgres", "99-demo-seed.sql"));
    var manifestPath = opts.GetValueOrDefault("manifest",
        Path.Combine(repoRoot, "tools", "StatsTid.DemoSeed", $"demo-manifest.{scale}.json"));

    Console.WriteLine($"generate: scale={scale} seed={seed} referenceDate={referenceDate:yyyy-MM-dd}");

    DemoDataset dataset;
    try
    {
        dataset = new DemoGenerator(scale, seed, referenceDate).Generate();
    }
    catch (InvalidOperationException ex)
    {
        throw new DisjointnessException(ex.Message);
    }

    var sql = SqlEmitter.Emit(dataset);
    var manifestJson = JsonSerializer.Serialize(dataset.Manifest, DemoManifestJsonContext.Default.DemoManifest);

    // Deterministic write: LF line endings, UTF-8 no BOM.
    await File.WriteAllTextAsync(outSql, sql.Replace("\r\n", "\n"), new UTF8Encoding(false));
    await File.WriteAllTextAsync(manifestPath, manifestJson.Replace("\r\n", "\n"), new UTF8Encoding(false));

    Console.WriteLine($"  orgs={dataset.Orgs.Count} users={dataset.Users.Count} employeeRoles={dataset.EmployeeRoles.Count} privilegedRoles={dataset.PrivilegedRoles.Count}");
    Console.WriteLine($"  reportingEdges={dataset.Manifest.ReportingEdges.Count} apiRoleGrants={dataset.Manifest.RoleGrants.Count} (privileged roles are SQL-seeded — grant API has a product bug)");
    Console.WriteLine($"  profileEdits={dataset.Manifest.ProfileEdits.Count} activity={dataset.Manifest.Activity.Count} vikars={dataset.Manifest.Vikars.Count} messyCases={dataset.Manifest.MessyCases.Count}");
    foreach (var t in dataset.Manifest.Trees)
        Console.WriteLine($"  tree {t.OrganisationId}: orgs={t.OrgCount} users={t.UserCount} managers={t.ManagerCount} maxDepth={t.MaxDepth} root={t.RootEmployeeId}");
    Console.WriteLine($"  wrote SQL → {outSql}");
    Console.WriteLine($"  wrote manifest → {manifestPath}");
    return 0;
}

// ── load ──
static async Task<int> RunLoadAsync(Dictionary<string, string> opts)
{
    var baseUrl = opts.GetValueOrDefault("base-url", "http://localhost:5100");
    var batchSize = int.TryParse(opts.GetValueOrDefault("batch-size", "200"), out var b) ? b : 200;
    var scale = opts.GetValueOrDefault("scale", "full");
    var repoRoot = FindRepoRoot();
    var manifestPath = opts.GetValueOrDefault("manifest",
        Path.Combine(repoRoot, "tools", "StatsTid.DemoSeed", $"demo-manifest.{scale}.json"));

    if (!File.Exists(manifestPath))
    {
        Console.Error.WriteLine($"Manifest not found: {manifestPath}. Run `generate` first.");
        return 2;
    }

    var json = await File.ReadAllTextAsync(manifestPath);
    var manifest = JsonSerializer.Deserialize(json, DemoManifestJsonContext.Default.DemoManifest)
                   ?? throw new InvalidOperationException("Manifest deserialised to null");

    Console.WriteLine($"load: baseUrl={baseUrl} batchSize={batchSize} manifest={manifestPath} (scale={manifest.Scale})");

    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
    using var api = new ApiClient(baseUrl);
    var loader = new DemoLoader(api, manifest, batchSize, Console.WriteLine);

    DemoLoader.LoadResult result;
    try
    {
        result = await loader.LoadAsync(cts.Token);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"LOAD FAILED: {ex.Message}");
        return 4;
    }

    Console.WriteLine("── load summary ──");
    Console.WriteLine($"  edges: imported={result.EdgesImported} skipped={result.EdgesSkipped}");
    Console.WriteLine($"  roles: granted={result.RolesGranted} skipped={result.RolesSkipped}");
    Console.WriteLine($"  profiles: set={result.ProfilesSet} skipped={result.ProfilesSkipped}");
    Console.WriteLine($"  activity: absences={result.AbsencesSaved} submitted={result.PeriodsSubmitted} approved={result.PeriodsApproved} rejected={result.PeriodsRejected}");
    Console.WriteLine($"  vikars: created={result.VikarsCreated} skipped={result.VikarsSkipped}");
    Console.WriteLine($"  messyCases: {result.MessyApplied}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine($"  WARNINGS ({result.Warnings.Count}):");
        foreach (var w in result.Warnings.Take(40))
            Console.WriteLine($"    - {w}");
        if (result.Warnings.Count > 40)
            Console.WriteLine($"    … and {result.Warnings.Count - 40} more");
    }

    // Optional post-load verification.
    if (opts.TryGetValue("db-conn", out var connStr) || opts.ContainsKey("verify"))
    {
        connStr ??= "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";
        Console.WriteLine("── post-load verification ──");
        var verifier = new DemoVerifier(connStr, Console.WriteLine);
        var verifyOk = await verifier.VerifyAsync(cts.Token);
        if (!verifyOk)
        {
            Console.Error.WriteLine("VERIFICATION FAILED");
            return 5;
        }
        Console.WriteLine("verification: ALL CHECKS PASSED");
    }

    return 0;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 2;
}

static int UsageExit()
{
    PrintUsage();
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        StatsTid.DemoSeed — deterministic demo-data generator + API loader (S84)

        Usage:
          generate --scale {smoke|full} [--out <sql>] [--manifest <json>]
                   [--seed N] [--reference-date YYYY-MM-DD]
          load --scale {smoke|full} [--manifest <json>] [--base-url URL]
               [--batch-size N] [--db-conn "<connstr>"] [--verify]

        Defaults: --scale full · --seed 42 · --reference-date 2026-06-15 ·
                  --out docker/postgres/99-demo-seed.sql ·
                  --manifest tools/StatsTid.DemoSeed/demo-manifest.{scale}.json ·
                  --base-url http://localhost:5100 · --batch-size 200
        """);
}

// ── option parsing: --key value | --flag ──
static Dictionary<string, string> ParseOptions(string[] argv)
{
    var d = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--", StringComparison.Ordinal)) continue;
        var key = argv[i][2..];
        if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            d[key] = argv[i + 1];
            i++;
        }
        else
        {
            d[key] = "true"; // bare flag
        }
    }
    return d;
}

// Walk up from the executable to find the repo root (the dir containing StatsTid.sln).
static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "StatsTid.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }
    // Fallback to cwd.
    return Directory.GetCurrentDirectory();
}

internal sealed class DisjointnessException : Exception
{
    public DisjointnessException(string message) : base(message) { }
}
