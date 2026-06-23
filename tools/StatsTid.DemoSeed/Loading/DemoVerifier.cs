using Npgsql;

namespace StatsTid.Tools.DemoSeed.Loading;

/// <summary>
/// S84 / TASK-8404 — post-load tree-invariant + isolation SQL checks. Confirms the demo data
/// landed correctly AND that the baseline 19-user fixture is untouched. Used by <c>load --verify</c>
/// and the smoke self-validation. NOT relied on for one-root enforcement at write time (the import
/// API does not enforce root-count) — this is the explicit invariant pass the plan requires.
/// </summary>
public sealed class DemoVerifier
{
    private readonly string _connStr;
    private readonly Action<string> _log;

    public DemoVerifier(string connStr, Action<string> log)
    {
        _connStr = connStr;
        _log = log;
    }

    public async Task<bool> VerifyAsync(CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_connStr);
        await conn.OpenAsync(ct);

        var ok = true;

        // 1. Baseline isolation — exactly 19 SYSTEM/baseline users + 13 SYSTEM reporting rows.
        var baselineUsers = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM users WHERE user_id IN " +
            "('admin01','admin02','ladm01','ladm02','hr01','hr02','mgr01','mgr02','mgr03'," +
            "'emp001','emp002','emp003','emp004','emp005','emp006','emp007','emp008','emp009','emp010')", ct);
        ok &= Check("baseline 19 users present", baselineUsers == 19, $"found {baselineUsers}");

        var systemReportingRows = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM reporting_lines WHERE created_by = 'SYSTEM'", ct);
        ok &= Check("baseline SeedData_Has13Rows intact", systemReportingRows == 13, $"found {systemReportingRows}");

        // 2. Demo orgs + users present.
        var demoOrgs = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM organizations WHERE org_id LIKE 'STYX%' OR org_id = 'MINX'", ct);
        ok &= Check("demo orgs present", demoOrgs > 0, $"found {demoOrgs}");

        var demoUsers = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM users WHERE user_id LIKE 'demo\\_%'", ct);
        ok &= Check("demo users present", demoUsers > 0, $"found {demoUsers}");

        // 2b. Privileged demo roles present (SQL-seeded; needed for dashboards/approvals/vikar).
        var demoLeaderRoles = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM role_assignments WHERE assigned_by='DEMO_SEED' AND role_id IN ('LOCAL_LEADER','LOCAL_HR')", ct);
        ok &= Check("demo privileged roles present", demoLeaderRoles > 0, $"found {demoLeaderRoles}");

        var demoGlobalAdmin = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM role_assignments WHERE assigned_by='DEMO_SEED' AND role_id='GLOBAL_ADMIN'", ct);
        ok &= Check("demo GLOBAL_ADMIN present", demoGlobalAdmin == 1, $"found {demoGlobalAdmin}");

        // 3. Tree invariant: exactly ONE root per organisation_id among DEMO reporting rows.
        //    A "root" = an employee in the tree that has NO active PRIMARY outgoing edge but is a
        //    manager of someone. We derive it as: managers that never appear as an employee_id.
        await using (var cmd = new NpgsqlCommand(
            """
            WITH demo AS (
                SELECT employee_id, manager_id, organisation_id
                FROM reporting_lines
                WHERE relationship = 'PRIMARY' AND effective_to IS NULL
                  AND created_by <> 'SYSTEM'
                  AND organisation_id LIKE 'STYX%'
            ),
            roots AS (
                SELECT DISTINCT organisation_id, manager_id
                FROM demo d
                WHERE NOT EXISTS (SELECT 1 FROM demo e WHERE e.employee_id = d.manager_id)
            )
            SELECT organisation_id, COUNT(*) AS root_count
            FROM roots
            GROUP BY organisation_id
            ORDER BY organisation_id
            """, conn))
        {
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            var anyTree = false;
            while (await rdr.ReadAsync(ct))
            {
                anyTree = true;
                var tree = rdr.GetString(0);
                var rootCount = rdr.GetInt64(1);
                ok &= Check($"tree {tree}: exactly one root", rootCount == 1, $"root_count={rootCount}");
            }
            ok &= Check("at least one demo tree has edges", anyTree, "no demo PRIMARY edges found");
        }

        // 4. No PRIMARY cycle among demo edges (self-edge or 2-cycle quick check; full cycle is
        //    enforced by the import API's recursive-CTE guard at write time).
        var selfEdges = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM reporting_lines WHERE relationship='PRIMARY' AND effective_to IS NULL " +
            "AND employee_id = manager_id AND created_by <> 'SYSTEM'", ct);
        ok &= Check("no demo self-edges", selfEdges == 0, $"found {selfEdges}");

        // 5. tree_root parity: every demo edge's organisation_id is a STYX root, which is an
        //    ORGANISATION post-S92 flatten (ADR-035 — STYRELSE→ORGANISATION; no AFDELING/TEAM).
        var badTreeRoots = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM reporting_lines r WHERE r.created_by <> 'SYSTEM' " +
            "AND r.organisation_id LIKE 'STYX%' " +
            "AND NOT EXISTS (SELECT 1 FROM organizations o WHERE o.org_id = r.organisation_id AND o.org_type='ORGANISATION')", ct);
        ok &= Check("demo edges' tree_root is an ORGANISATION", badTreeRoots == 0, $"found {badTreeRoots} bad");

        // 6. Flatten parity (S92 / ADR-035): every demo user's primary_org is an ORGANISATION
        //    (never a MAO, never a removed AFDELING/TEAM); no demo org row carries a retired type.
        var demoUsersOffOrganisation = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM users u WHERE u.user_id LIKE 'demo\\_%' " +
            "AND NOT EXISTS (SELECT 1 FROM organizations o WHERE o.org_id = u.primary_org_id AND o.org_type='ORGANISATION')", ct);
        ok &= Check("demo users' primary_org is an ORGANISATION", demoUsersOffOrganisation == 0, $"found {demoUsersOffOrganisation} off-Organisation");

        var demoOrgsBadType = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM organizations WHERE (org_id LIKE 'STYX%' OR org_id = 'MINX') " +
            "AND org_type NOT IN ('MAO','ORGANISATION')", ct);
        ok &= Check("demo orgs carry only MAO/ORGANISATION types", demoOrgsBadType == 0, $"found {demoOrgsBadType} bad-type");

        // 7. enhed_label presence (S92 / ADR-035): the moved-up rank-and-file carry a display-only
        //    enhed_label (the former AFDELING/TEAM unit name). At least one such row must exist.
        var demoEnhedLabels = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM employee_profiles p WHERE p.employee_id LIKE 'demo\\_%' " +
            "AND p.effective_to IS NULL AND p.enhed_label IS NOT NULL", ct);
        ok &= Check("demo enhed_label profiles present", demoEnhedLabels > 0, $"found {demoEnhedLabels}");

        // 8. Scope-flatten parity (S93 / ADR-035): ORG_AND_DESCENDANTS is DROPPED. Every demo role
        //    row must be GLOBAL or ORG_ONLY — ZERO ORG_AND_DESCENDANTS. The backend init.sql CHECK
        //    (scope_type IN ('GLOBAL','ORG_ONLY')) enforces this at write time; this is the explicit
        //    data assertion.
        var demoOrgAndDescendants = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM role_assignments WHERE assigned_by='DEMO_SEED' AND scope_type='ORG_AND_DESCENDANTS'", ct);
        ok &= Check("zero demo ORG_AND_DESCENDANTS rows", demoOrgAndDescendants == 0, $"found {demoOrgAndDescendants}");

        // 9. Every demo ORG_ONLY role row's org_id is an ORGANISATION (never a MAO root, never NULL).
        //    Covers the HR/leader/employee scopes — all rooted on the Organisation post-flatten.
        var demoBadScopeOrg = await ScalarLongAsync(conn,
            "SELECT COUNT(*) FROM role_assignments ra WHERE ra.assigned_by='DEMO_SEED' AND ra.scope_type='ORG_ONLY' " +
            "AND NOT EXISTS (SELECT 1 FROM organizations o WHERE o.org_id = ra.org_id AND o.org_type='ORGANISATION')", ct);
        ok &= Check("demo ORG_ONLY scopes resolve to an ORGANISATION", demoBadScopeOrg == 0, $"found {demoBadScopeOrg} bad");

        return ok;
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var res = await cmd.ExecuteScalarAsync(ct);
        return res is long l ? l : Convert.ToInt64(res);
    }

    private bool Check(string name, bool pass, string detail)
    {
        _log($"  [{(pass ? "PASS" : "FAIL")}] {name} ({detail})");
        return pass;
    }
}
