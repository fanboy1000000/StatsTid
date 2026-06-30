using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

namespace StatsTid.Backend.Api;

/// <summary>
/// S111 / TASK-11101 — writes the OpenAPI v3 document to disk for the <c>--openapi</c> doc-only
/// entrypoint. Resolves Swashbuckle's <see cref="ISwaggerProvider"/> and serializes the generated
/// document. This NEVER touches the database — it reads only the mapped endpoint metadata + the
/// <c>.Produces</c>/<c>.Accepts</c> declarations (the handlers' DI dependencies are resolved
/// per-request and are never invoked here), which is why the entrypoint can run Docker-free.
/// </summary>
public static class OpenApiSpecGenerator
{
    /// <summary>Generate the v1 document and write it to the resolved output path (the committed
    /// <c>docs/api/openapi.json</c>, or an explicit path passed after <c>--openapi</c>, which
    /// <c>tools/check_openapi_sync.py</c> uses to regenerate into a temp file).</summary>
    public static async Task WriteAsync(WebApplication app, string[] args)
    {
        // The endpoints mapped via app.MapXxx live in the WebApplication's OWN route-builder DataSources,
        // but ApiExplorer (and thus Swashbuckle) reads the DI-registered EndpointDataSource — and the two
        // are only linked when the host starts (ConfigureApplication → UseEndpoints). We do that link here
        // explicitly (no Kestrel, no DB): UseRouting + UseEndpoints surfaces the route-builder's data
        // sources into the DI composite source, then we build the pipeline delegate.
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            foreach (var ds in ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.ToList())
                endpoints.DataSources.Add(ds);
        });
        ((IApplicationBuilder)app).Build();

        var provider = app.Services.GetRequiredService<ISwaggerProvider>();
        var document = provider.GetSwagger("v1");

        var outputPath = ResolveOutputPath(args);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using (var stream = File.Create(outputPath))
        await using (var textWriter = new StreamWriter(stream))
        {
            // Pretty-printed JSON (OpenApiJsonWriter default) so the committed spec is reviewable +
            // diffs cleanly; the drift gate compares PARSED JSON so formatting is not load-bearing.
            var jsonWriter = new OpenApiJsonWriter(textWriter);
            document.SerializeAsV3(jsonWriter);
            await textWriter.FlushAsync();
        }

        Console.WriteLine($"[openapi] wrote {outputPath}");
        app.Logger.LogInformation("OpenAPI spec written to {Path}", outputPath);
    }

    /// <summary>The committed spec path, or an explicit override: the first non-flag token after
    /// <c>--openapi</c> (so <c>-- --openapi C:\tmp\spec.json</c> targets a temp file).</summary>
    internal static string ResolveOutputPath(string[] args)
    {
        var idx = Array.IndexOf(args, "--openapi");
        if (idx >= 0 && idx + 1 < args.Length && !args[idx + 1].StartsWith('-'))
            return Path.GetFullPath(args[idx + 1]);

        return Path.Combine(FindRepoRoot(), "docs", "api", "openapi.json");
    }

    /// <summary>Walk up from the bin directory to the repo root (the folder holding StatsTid.sln) so
    /// the default path is correct regardless of the process working directory.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StatsTid.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
