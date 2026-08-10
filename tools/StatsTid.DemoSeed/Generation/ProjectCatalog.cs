using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tools.DemoSeed.Generation;

/// <summary>
/// S127 / TASK-12701a — the per-organisation project catalogue.
///
/// <para>Owner ruling B (REFINEMENT submit-allocation-gate §7): <i>all organisations should have
/// projects</i>. Projects are strictly org-scoped (<c>ProjectRepository.GetByOrgAsync</c> filters on
/// <c>org_id</c> AND <c>is_active</c>), so an employee in a project-less org sees an EMPTY project
/// set and can never satisfy the submit-time allocation gate. Every org the generator emits —
/// the MAO root included, since <c>demo_admin</c> homes there — gets this same four-project
/// catalogue. Codes only need to be unique WITHIN an org (the <c>projects</c> UNIQUE
/// <c>(org_id, project_code)</c>), so one shared template set is enough.</para>
///
/// <para>Static data, no RNG, no wall-clock: adding it cannot perturb the generator's seeded draw
/// order.</para>
/// </summary>
internal static class ProjectCatalog
{
    internal readonly record struct Template(string Code, string Name);

    /// <summary>The catalogue every org receives, in display order. FOUR entries is the floor that
    /// keeps a two-project split day from ever booking the same code twice
    /// (<see cref="AllocatedMonthBuilder"/>).</summary>
    internal static readonly Template[] PerOrg =
    {
        new("DRIFT-01", "Daglig drift"),
        new("UDV-01", "Udvikling og implementering"),
        new("ADM-01", "Administration og ledelse"),
        new("PROJ-01", "Tvaergaaende projekt"),
    };

    /// <summary>The catalogue for one org, in <c>sort_order</c> order (1-based).</summary>
    internal static IEnumerable<DemoProject> ForOrg(string orgId)
        => PerOrg.Select((t, i) => new DemoProject
        {
            OrgId = orgId,
            ProjectCode = t.Code,
            ProjectName = t.Name,
            SortOrder = i + 1,
        });

    /// <summary>The catalogue for every org, org-order preserved (the caller passes the orgs in
    /// generation order, so the emitted SQL block is deterministic).</summary>
    internal static List<DemoProject> ForOrgs(IEnumerable<string> orgIds)
        => orgIds.SelectMany(ForOrg).ToList();

    /// <summary>The project codes available to an employee homed in <paramref name="orgId"/>, in
    /// display order.</summary>
    internal static IReadOnlyList<string> CodesFor(string orgId, IReadOnlyList<DemoProject> projects)
        => projects.Where(p => p.OrgId == orgId).Select(p => p.ProjectCode).ToList();
}
