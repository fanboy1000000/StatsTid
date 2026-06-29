using System.Text.Json.Serialization;

namespace StatsTid.Tools.DemoSeed.Model;

/// <summary>
/// S84 / TASK-8401 — the deterministic JSON manifest emitted by <c>generate</c> and
/// consumed by <c>load</c>. The STRUCTURAL layer (orgs + users + bulk EMPLOYEE roles +
/// the demo GLOBAL_ADMIN bootstrap) is carried by <c>99-demo-seed.sql</c>; this manifest
/// carries everything the post-boot API loader needs: the reporting edges, the privileged
/// role grants, the part-time/position subset, the activity plan, and the messy cases.
///
/// All dates are derived from a fixed reference date (NOT wall-clock) so the manifest is
/// byte-reproducible for a given (seed, scale).
/// </summary>
public sealed class DemoManifest
{
    /// <summary>Scale this manifest was generated at ("smoke" | "full").</summary>
    public string Scale { get; set; } = "";

    /// <summary>The fixed RNG seed used (recorded for provenance / determinism proof).</summary>
    public int Seed { get; set; }

    /// <summary>The fixed reference date all generated dates are derived from (ISO yyyy-MM-dd).</summary>
    public string ReferenceDate { get; set; } = "";

    /// <summary>The demo GLOBAL_ADMIN username the loader authenticates as.</summary>
    public string AdminUserId { get; set; } = "";

    /// <summary>The shared dev password for every demo user (matches the baseline 'password').</summary>
    public string Password { get; set; } = "";

    /// <summary>Per-tree summary (root org id + counts) — drives the import batches + verification.</summary>
    public List<DemoTree> Trees { get; set; } = new();

    /// <summary>The reporting edges to load via POST /api/admin/reporting-lines/import (PRIMARY).</summary>
    public List<DemoReportingEdge> ReportingEdges { get; set; } = new();

    /// <summary>Privileged role grants (LOCAL_HR / LOCAL_LEADER) via POST /api/admin/roles/grant.</summary>
    public List<DemoRoleGrant> RoleGrants { get; set; } = new();

    /// <summary>The ~10% part-time/position subset via PUT /api/admin/employee-profiles/{id}.</summary>
    public List<DemoProfileEdit> ProfileEdits { get; set; } = new();

    /// <summary>The light activity plan (~10-20% of users): absences + periods in mixed states.</summary>
    public List<DemoActivity> Activity { get; set; } = new();

    /// <summary>Vikar (stand-in) assignments via POST /api/admin/reporting-lines/{managerId}/vikar.</summary>
    public List<DemoVikar> Vikars { get; set; } = new();

    /// <summary>The ~20-30 hand-curated "messy" cases (scripted steps over the API).</summary>
    public List<DemoMessyCase> MessyCases { get; set; } = new();
}

public sealed class DemoTree
{
    public string OrganisationId { get; set; } = "";
    public string RootEmployeeId { get; set; } = "";
    public int OrgCount { get; set; }
    public int UserCount { get; set; }
    public int ManagerCount { get; set; }
    public int MaxDepth { get; set; }
}

public sealed class DemoReportingEdge
{
    public string EmployeeId { get; set; } = "";
    public string ManagerId { get; set; } = "";
    public string OrganisationId { get; set; } = "";

    /// <summary>Always "PRIMARY" for the structural tree (vikar/ACTING is separate, see <see cref="DemoVikar"/>).</summary>
    public string Relationship { get; set; } = "PRIMARY";

    /// <summary>ISO yyyy-MM-dd effective-from for the import row.</summary>
    public string EffectiveFrom { get; set; } = "";
}

public sealed class DemoRoleGrant
{
    public string UserId { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string OrgId { get; set; } = "";
    public string ScopeType { get; set; } = "";
}

public sealed class DemoProfileEdit
{
    public string EmployeeId { get; set; } = "";
    public decimal PartTimeFraction { get; set; }
    public string Position { get; set; } = "";
}

/// <summary>One activity script for one employee: a set of absences + an optional period transition.</summary>
public sealed class DemoActivity
{
    public string EmployeeId { get; set; } = "";
    public string OrgId { get; set; } = "";
    public string AgreementCode { get; set; } = "";
    public string OkVersion { get; set; } = "";

    /// <summary>The month the activity falls in (year/month drive the skema save + the period bounds).</summary>
    public int Year { get; set; }
    public int Month { get; set; }

    public List<DemoAbsence> Absences { get; set; } = new();

    /// <summary>
    /// Period lifecycle: "NONE" | "SUBMITTED" | "APPROVED" | "REJECTED".
    /// SUBMITTED leaves the period in SUBMITTED; APPROVED submits then approves; REJECTED submits then rejects.
    /// </summary>
    public string PeriodOutcome { get; set; } = "NONE";
}

public sealed class DemoAbsence
{
    /// <summary>ISO yyyy-MM-dd (non-boundary weekday, derived from the reference date).</summary>
    public string Date { get; set; } = "";

    /// <summary>One of SICK_DAY / VACATION / CARE_DAY.</summary>
    public string AbsenceType { get; set; } = "";

    public decimal Hours { get; set; }
}

public sealed class DemoVikar
{
    public string ManagerId { get; set; } = "";
    public string VikarUserId { get; set; } = "";

    /// <summary>ISO yyyy-MM-dd inclusive last-covered day (must be after today at load time).</summary>
    public string EffectiveTo { get; set; } = "";

    public string Reason { get; set; } = "FERIE";
}

/// <summary>
/// A scripted "messy" case. <see cref="Kind"/> tags the scenario; <see cref="EmployeeId"/> +
/// <see cref="Note"/> describe it. The loader applies the corresponding API steps idempotently.
/// </summary>
public sealed class DemoMessyCase
{
    public string Kind { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public string Note { get; set; } = "";

    /// <summary>Optional secondary actor (e.g. cross-styrelse transfer target manager).</summary>
    public string? RelatedId { get; set; }

    /// <summary>Optional scalar payload (e.g. odd part-time fraction, new agreement code).</summary>
    public string? Value { get; set; }
}

/// <summary>
/// System.Text.Json source-generation context for deterministic, allocation-light
/// (de)serialization. Indented + camelCase for a human-readable, diff-stable manifest.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DemoManifest))]
public partial class DemoManifestJsonContext : JsonSerializerContext
{
}
