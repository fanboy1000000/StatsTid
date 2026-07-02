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

    /// <summary>
    /// S114 / TASK-11400 — the per-Organisation derived unit spines (ADR-038: direktion › omrade ›
    /// kontor › team › enhed), loaded via the REAL units admin APIs in the canonical stage order
    /// (units parent-first → home ALL members probe-first → appoint leaders LAST — the D3
    /// re-home-strips-leadership invariant makes any other order self-corrupting).
    /// NULL (and hence ABSENT from the JSON — <see cref="JsonIgnoreCondition.WhenWritingNull"/>)
    /// when no tree carries a unit-spine override: the legacy no-override manifest stays
    /// byte-identical to the pre-S114 golden artifact, and old manifests deserialize fine.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DemoUnitPlan>? UnitPlans { get; set; }
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
/// S114 — ONE Organisation's derived unit-spine plan: the units (keyed by their ANCHOR manager's
/// user id — server GUIDs are assigned at load time and resolved by (org, parent-chain, name)),
/// the manager-depth histogram, and the DELIBERATE messiness ledger the verifier asserts EXACTLY.
/// </summary>
public sealed class DemoUnitPlan
{
    public string OrganisationId { get; set; } = "";

    /// <summary>Managers per depth, index = depth 0..4 (the generation-time assertion guarantees
    /// length 5 with every entry ≥ 1 — the manifest depth spot-proof).</summary>
    public List<int> ManagersPerDepth { get; set; } = new();

    /// <summary>Every unit, parent-first-orderable via <see cref="DemoUnit.Depth"/>.</summary>
    public List<DemoUnit> Units { get; set; } = new();

    /// <summary>The deliberately-LEADERLESS units (the loader SKIPS their leader appointment).
    /// The verifier asserts the live leaderless count equals this count EXACTLY (catching any
    /// accidental decapitation via the D3 re-home leadership strip).</summary>
    public List<string> LeaderlessUnitKeys { get; set; } = new();

    /// <summary>The deliberate cross-unit sideways-homed members (NON-manager leaves ONLY — the
    /// D3 hard rule; disjoint from each other and from the leaderless units).</summary>
    public List<DemoSidewaysCase> SidewaysCases { get; set; } = new();
}

/// <summary>S114 — one derived unit. <see cref="UnitKey"/> = the anchor manager's user id (stable,
/// manifest-local); <see cref="MemberUserIds"/> = the anchor + their NON-manager reports (a
/// single-unit-membership PARTITION of the org's active users — manager-reports appear as CHILD
/// units, not member rows), post-messiness (sideways members appear in their TARGET unit's list).</summary>
public sealed class DemoUnit
{
    public string UnitKey { get; set; } = "";

    /// <summary>The parent unit's key (the anchor's manager) — null for the depth-0 direktion.</summary>
    public string? ParentUnitKey { get; set; }

    /// <summary>direktion | omrade | kontor | team | enhed — [depth].</summary>
    public string Type { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Manager/unit depth 0..4 (== the type's rank position; PARTIAL-RANK-valid by
    /// construction: every child is exactly one rank deeper).</summary>
    public int Depth { get; set; }

    /// <summary>The unit's leader (the anchor manager) — null ⇒ deliberately leaderless.</summary>
    public string? LeaderUserId { get; set; }

    public List<string> MemberUserIds { get; set; } = new();
}

/// <summary>S114 — one deliberate cross-unit case: a NON-manager leaf of <see cref="FromUnitKey"/>
/// homed sideways into <see cref="ToUnitKey"/> (which keeps a leader — the amber "Ret" flow has a
/// valid in-unit target).</summary>
public sealed class DemoSidewaysCase
{
    public string UserId { get; set; } = "";
    public string FromUnitKey { get; set; } = "";
    public string ToUnitKey { get; set; } = "";
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
