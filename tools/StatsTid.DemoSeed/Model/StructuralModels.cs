namespace StatsTid.Tools.DemoSeed.Model;

/// <summary>Generated organisation row (maps 1:1 to the organizations INSERT).</summary>
public sealed class DemoOrg
{
    public required string OrgId { get; init; }
    public required string OrgName { get; init; }

    /// <summary>
    /// S92 / ADR-035 — the flattened taxonomy: <c>MAO</c> (root) | <c>ORGANISATION</c>
    /// (under a MAO). The former MINISTRY/STYRELSE/AFDELING/TEAM 4-tier tree is retired:
    /// MINISTRY→MAO, STYRELSE→ORGANISATION, and the AFDELING/TEAM leaf orgs are dropped
    /// (every user homes directly on its Organisation; S103 / ADR-038 retired the legacy
    /// <c>enhed_label</c> model the leaf names briefly mapped to).
    /// </summary>
    public required string OrgType { get; init; }

    public required string? ParentOrgId { get; init; }
    public required string MaterializedPath { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }

    /// <summary>The ORGANISATION-tree root org id this org belongs under (for tree grouping).</summary>
    public required string OrganisationId { get; init; }

    /// <summary>Depth within the tree (root MAO = 0, ORGANISATION = 1).</summary>
    public required int Depth { get; init; }
}

/// <summary>Generated user row (only real <c>users</c> columns — NO part_time_fraction/position).</summary>
public sealed class DemoUser
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string PasswordHash { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public required string PrimaryOrgId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required string EmploymentCategory { get; init; }

    /// <summary>ISO yyyy-MM-dd.</summary>
    public required string BirthDate { get; init; }

    /// <summary>ISO yyyy-MM-dd.</summary>
    public required string EmploymentStartDate { get; init; }

    /// <summary>ISO yyyy-MM-dd, or null for non-leavers.</summary>
    public required string? EmploymentEndDate { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>The ORGANISATION-tree root org id (for tree grouping). Equals <see cref="PrimaryOrgId"/>
    /// post-S92 flatten — every demo user sits directly on their Organisation.</summary>
    public required string OrganisationId { get; init; }

    /// <summary>True if this user manages at least one direct report (gets a LOCAL_LEADER grant).</summary>
    public bool IsManager { get; set; }
}

/// <summary>
/// A SQL-seeded role_assignments row (event-less, matching the baseline init.sql:894 pattern;
/// assigned_by='DEMO_SEED'). Used for the bulk EMPLOYEE rows AND — because the live
/// POST /api/admin/roles/grant endpoint has a pre-existing schema bug (its role_assignment_audit
/// INSERT targets non-existent columns; see SPRINT-84) — the privileged LOCAL_HR / LOCAL_LEADER
/// rows. Authorization reads role_assignments table-direct (login derives JWT scopes from it), so
/// these rows yield working scopes; the audit/event parity the API path would have added is the
/// documented scoped limitation (a follow-up once the grant endpoint is fixed).
/// </summary>
public sealed class DemoRoleRow
{
    public required string UserId { get; init; }
    public required string RoleId { get; init; }

    /// <summary>NULL for a GLOBAL scope.</summary>
    public required string? OrgId { get; init; }

    /// <summary>GLOBAL | ORG_ONLY.</summary>
    public required string ScopeType { get; init; }
}

/// <summary>
/// S127 / TASK-12701a — a generated <c>projects</c> row (maps 1:1 to the projects INSERT).
///
/// <para><b>Why this exists.</b> S127 makes a month un-sendable unless every worked hour is
/// allocated to a project, and projects are strictly ORG-SCOPED
/// (<c>ProjectRepository.GetByOrgAsync</c>). A user in a project-less org therefore has an EMPTY
/// project set — not a filtered one — and can never allocate. Owner ruling B (REFINEMENT
/// submit-allocation-gate §7): <i>all organisations should have projects</i>. So the generator
/// emits a project catalogue for EVERY org it creates, the MAO included (<c>demo_admin</c> homes
/// there).</para>
///
/// <para><c>project_id</c> is deliberately NOT carried: the column defaults to
/// <c>gen_random_uuid()</c>, nothing in the seed or the loader references a project by id (the
/// Skema save keys allocations on <c>project_code</c> — <c>SkemaEndpoints.cs:1609</c> writes
/// <c>TaskId = entry.ProjectCode</c>), and omitting it keeps the emitted SQL byte-stable.</para>
/// </summary>
public sealed class DemoProject
{
    public required string OrgId { get; init; }

    /// <summary>Unique within the org (the <c>projects</c> UNIQUE (org_id, project_code)).</summary>
    public required string ProjectCode { get; init; }

    public required string ProjectName { get; init; }

    /// <summary>1-based display order within the org.</summary>
    public required int SortOrder { get; init; }
}

/// <summary>The full generated dataset (the SQL artifact + the manifest are both derived from this).</summary>
public sealed class DemoDataset
{
    public required List<DemoOrg> Orgs { get; init; }
    public required List<DemoUser> Users { get; init; }

    /// <summary>S127 — the per-org project catalogue (every org, MAO included; see <see cref="DemoProject"/>).</summary>
    public required List<DemoProject> Projects { get; init; }

    /// <summary>Bulk EMPLOYEE rows (every demo user).</summary>
    public required List<DemoRoleRow> EmployeeRoles { get; init; }

    /// <summary>Privileged LOCAL_HR / LOCAL_LEADER rows (SQL-seeded; see <see cref="DemoRoleRow"/>).</summary>
    public required List<DemoRoleRow> PrivilegedRoles { get; init; }

    public required DemoManifest Manifest { get; init; }
}
