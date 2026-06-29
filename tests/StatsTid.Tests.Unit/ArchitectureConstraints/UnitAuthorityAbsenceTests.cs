using System.Text.RegularExpressions;

namespace StatsTid.Tests.Unit.ArchitectureConstraints;

/// <summary>
/// SPRINT-103 / TASK-10305 (Enhedsspor Phase 1a) — the by-construction authority-absence guard
/// (ADR-038 D5 / P7): <c>units</c> / <c>users.unit_id</c> / <c>unit_leaders</c> carry NO <b>SCOPE</b>.
/// Role-scope stays anchored at the Organisation (<c>primary_org_id</c> + exact <c>CoversOrg</c> match);
/// the unit dimension grants no deep-tree / subtree scope.
///
/// <para>
/// <b>S105 / ADR-038 D4 SPLIT.</b> The SCOPE guard below (this test) KEEPS
/// <c>OrgScopeValidator</c> / <c>RoleScope.CoversOrg</c> / the <c>ValidateEmployeeAccess</c> path
/// strictly unit-free — the surviving keystone assertion (units grant NO scope; a parent/ancestor-unit
/// leader gets NOTHING beyond the D4 direct-member edge). <c>DesignatedApproverAuthorizer</c> has been
/// REMOVED from the scanned set because S105 LEGITIMATELY wires the unit-leader EXCEPTION approval EDGE
/// there (D4: <c>unit_leaders(E.unit_id)</c> is now an authority-bearing, direct-member-bounded path).
/// The replacement for that file's coverage is the behavioural boundedness RED test (TASK-10503/10504:
/// a grandparent/parent/sibling-unit leader grants no <c>CanApprove</c> and appears in no dashboard
/// read for an <c>E.unit_id</c> member), NOT a text scan.
/// </para>
///
/// <para>
/// This is the structural, no-Docker proof (mirrors the S100 / ADR-036 "Enhed shares no authority"
/// guard): the canonical scope source files must contain NO reference to the unit-tree identifiers. If
/// a future change joins <c>units</c> / <c>unit_leaders</c> into (or reads <c>unit_id</c> within) the
/// SCOPE path, this test goes RED — forcing a deliberate ADR-038 decision rather than a silent
/// authority leak. File-text scan, the same technique as <c>PlannerBypassGuardTests</c> (anchored on
/// path + token, robust to signature churn).
/// </para>
/// </summary>
public sealed class UnitAuthorityAbsenceTests
{
    /// <summary>The canonical org-SCOPE source files. These decide WHO may see/act on WHOSE data by
    /// the Organisation boundary — the exact surface ADR-038 D5 keeps unit-free. NOTE (S105 / ADR-038
    /// D4): <c>DesignatedApproverAuthorizer</c> is DELIBERATELY NOT in this set — units legitimately
    /// enter the approval EDGE there (the direct-member unit-leader path), bounded by the behavioural
    /// boundedness RED test, not this scan. The SCOPE path stays unit-free.</summary>
    private static readonly string[] AuthorityPathFiles =
    {
        // OrgScopeValidator: ValidateEmployeeAccessAsync + ValidateOrgAccessAsync + the CoversOrg loop.
        "src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs",
        // RoleScope.CoversOrg: the exact-Organisation coverage predicate.
        "src/SharedKernel/StatsTid.SharedKernel/Security/RoleScope.cs",
    };

    /// <summary>The unit-tree identifiers that must NOT appear anywhere in the authority path: the
    /// column (<c>unit_id</c>), the leadership table (<c>unit_leaders</c>), the structure table
    /// (<c>units</c>), and — S104 widen — the PascalCase property/type reads (<c>UnitId</c>,
    /// <c>UnitLeaders</c>) that the snake_case tokens miss (a <c>.UnitId</c> read in an authority
    /// file would otherwise evade the guard). IgnoreCase covers the rest. Matched as whole-identifier
    /// tokens so a benign substring (e.g. "community", "unittest") never trips the guard.</summary>
    private static readonly Regex UnitTokenPattern =
        new(@"\b(unit_id|unit_leaders|units|UnitId|UnitLeaders)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void AuthorityAndScopePath_ContainsNoUnitReference_ByConstruction()
    {
        var repoRoot = LocateRepoRoot();
        var violations = new List<string>();

        foreach (var rel in AuthorityPathFiles)
        {
            var full = Path.Combine(repoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full),
                $"Authority-path file '{rel}' not found under repo root '{repoRoot}'. If it was " +
                "moved/renamed, update AuthorityPathFiles in UnitAuthorityAbsenceTests.");

            var text = File.ReadAllText(full);
            foreach (Match m in UnitTokenPattern.Matches(text))
            {
                var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                violations.Add($"  - {rel}:{line}  (matched '{m.Value}')");
            }
        }

        Assert.True(violations.Count == 0,
            "ADR-038 D5 / P7: the authority/scope/approval path must be unit-free (units/unit_id/" +
            "unit_leaders carry ZERO authority). The following reference(s) appeared — if a unit " +
            "dimension is being deliberately wired into authority (the S104 unit-leader exception " +
            "path), do it through an explicit ADR-038 amendment + update this guard:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>Sanity: the guarded files still exist AND still encode Organisation-anchored scope
    /// (so the guard is not a stale no-op pointing at moved files). We assert the scope predicate
    /// still reads <c>primary_org_id</c> / <c>CoversOrg</c> — the Organisation anchor ADR-038 keeps.</summary>
    [Fact]
    public void GuardedFiles_StillEncodeOrganisationAnchoredScope()
    {
        var repoRoot = LocateRepoRoot();

        var roleScope = File.ReadAllText(Path.Combine(repoRoot,
            "src/SharedKernel/StatsTid.SharedKernel/Security/RoleScope.cs".Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("CoversOrg", roleScope);

        var validator = File.ReadAllText(Path.Combine(repoRoot,
            "src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs".Replace('/', Path.DirectorySeparatorChar)));
        Assert.Contains("PrimaryOrgId", validator);
        Assert.Contains("CoversOrg", validator);
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root (directory containing *.sln) from test bin output. " +
            $"Searched upward from: {AppContext.BaseDirectory}");
    }
}
