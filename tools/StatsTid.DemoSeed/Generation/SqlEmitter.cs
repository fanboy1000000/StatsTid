using System.Globalization;
using System.Text;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tools.DemoSeed.Generation;

/// <summary>
/// S84 / TASK-8401 — emits the deterministic <c>99-demo-seed.sql</c> structural artifact:
/// organisations, users, the demo GLOBAL_ADMIN bootstrap (+ its GLOBAL role row), the bulk
/// EMPLOYEE role_assignments AND the privileged LOCAL_HR / LOCAL_LEADER role rows — all
/// event-less, matching the baseline init.sql:894 pattern. (The privileged grants are
/// SQL-seeded rather than issued via the API because <c>POST /api/admin/roles/grant</c> has a
/// pre-existing production defect — see SPRINT-84 Discovered Defects.)
///
/// S103 / TASK-10305 (Enhedsspor Phase 1a, ADR-038 D9): the legacy <c>enhed_label</c> column and the
/// <c>enheder</c> / <c>user_enheder</c> tables were dropped (replaced by <c>units</c> /
/// <c>unit_leaders</c> / <c>users.unit_id</c>), so this emitter no longer produces any Enhed SQL.
/// Demo users home directly at their ORGANISATION (<c>users.unit_id</c> stays NULL); the structural
/// unit tree + the units CRUD ship in S104 / Phase 3 (the modest demo unit tree under STY02 lives in
/// init.sql).
///
/// Only the reporting TREE + activity are LOADED POST-BOOT via the API (event-emitting; OQ-4)
/// and so are NOT in this file.
///
/// All INSERTs use ON CONFLICT DO NOTHING. The disjointness assertion in the generator
/// guarantees no baseline collision is silently dropped. Output is byte-stable for a given
/// dataset (rows already in deterministic generation order).
/// </summary>
public static class SqlEmitter
{
    private const string DemoAdminId = "demo_admin";

    public static string Emit(DemoDataset dataset)
    {
        var minId = dataset.Orgs[0].OrgId; // ministry is always row 0
        var sb = new StringBuilder();

        sb.Append(Header(dataset));

        // ── 1. Organisations ──
        sb.AppendLine("-- ── Organisations (S92 / ADR-035 flatten: 1 demo MAO root + N ORGANISATIONs under it; ──");
        sb.AppendLine("--    no AFDELING/TEAM org rows — deep structure lives in `units` from S104 / Phase 3) ──");
        sb.AppendLine("INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active) VALUES");
        AppendRows(sb, dataset.Orgs, (rb, o) =>
        {
            rb.Append('(')
              .Append(Lit(o.OrgId)).Append(", ")
              .Append(Lit(o.OrgName)).Append(", ")
              .Append(Lit(o.OrgType)).Append(", ")
              .Append(o.ParentOrgId is null ? "NULL" : Lit(o.ParentOrgId)).Append(", ")
              .Append(Lit(o.MaterializedPath)).Append(", ")
              .Append(Lit(o.AgreementCode)).Append(", ")
              .Append(Lit(o.OkVersion)).Append(", ")
              .Append("TRUE)");
        });
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        // ── 2. Demo GLOBAL_ADMIN user (the import API is GlobalAdminOnly) ──
        sb.AppendLine("-- ── Demo GLOBAL_ADMIN bootstrap user (the loader authenticates as this) ──");
        sb.AppendLine("INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, employment_category, is_active) VALUES");
        sb.Append("    (")
          .Append(Lit(DemoAdminId)).Append(", ")
          .Append(Lit(DemoAdminId)).Append(", ")
          .Append(Lit(DemoGenerator.PasswordHash)).Append(", ")
          .Append(Lit("Demo Global Admin")).Append(", ")
          .Append(Lit($"{DemoAdminId}@demo.dk")).Append(", ")
          .Append(Lit(minId)).Append(", ")
          .Append(Lit("AC")).Append(", ")
          .Append(Lit("OK24")).Append(", ")
          .Append(Lit("Kontorchef")).Append(", ")
          .AppendLine("TRUE)");
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        // ── 3. Demo users ──
        sb.AppendLine("-- ── Demo users (real users columns only; part_time_fraction/position set via the profile API) ──");
        sb.AppendLine("INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version, employment_category, birth_date, employment_start_date, employment_end_date, is_active) VALUES");
        AppendRows(sb, dataset.Users, (rb, u) =>
        {
            rb.Append('(')
              .Append(Lit(u.UserId)).Append(", ")
              .Append(Lit(u.Username)).Append(", ")
              .Append(Lit(u.PasswordHash)).Append(", ")
              .Append(Lit(u.DisplayName)).Append(", ")
              .Append(Lit(u.Email)).Append(", ")
              .Append(Lit(u.PrimaryOrgId)).Append(", ")
              .Append(Lit(u.AgreementCode)).Append(", ")
              .Append(Lit(u.OkVersion)).Append(", ")
              .Append(Lit(u.EmploymentCategory)).Append(", ")
              .Append("DATE ").Append(Lit(u.BirthDate)).Append(", ")
              .Append("DATE ").Append(Lit(u.EmploymentStartDate)).Append(", ")
              .Append(u.EmploymentEndDate is null ? "NULL" : "DATE " + Lit(u.EmploymentEndDate)).Append(", ")
              .Append(u.IsActive ? "TRUE)" : "FALSE)");
        });
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        // ── 3b. (RETIRED) Demo enhed_label / structured enheder pre-seed ──
        //    S103 / TASK-10305 (Enhedsspor Phase 1a, ADR-038 D9): the legacy
        //    employee_profiles.enhed_label COLUMN and the enheder / user_enheder TABLES were dropped
        //    (replaced by units / unit_leaders / users.unit_id). The demo no longer emits any of
        //    those — every demo user simply homes at its ORGANISATION (users.unit_id stays NULL; the
        //    EmployeeProfileSeeder backfills a plain profile on boot). The structural unit tree + the
        //    units CRUD that populates it ship in S104 / Phase 3; the modest demo unit tree under
        //    STY02 lives directly in init.sql.

        // ── 4. Demo GLOBAL_ADMIN role row (GLOBAL scope; login derives the JWT scopes from this) ──
        sb.AppendLine("-- ── Demo GLOBAL_ADMIN role assignment (GLOBAL scope) ──");
        sb.AppendLine("INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES");
        sb.Append("    (")
          .Append(Lit(DemoAdminId)).Append(", ")
          .Append(Lit("GLOBAL_ADMIN")).Append(", ")
          .Append("NULL, ")
          .Append(Lit("GLOBAL")).Append(", ")
          .Append(Lit("DEMO_SEED")).AppendLine(")");
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        // ── 5. Privileged LOCAL_HR / LOCAL_LEADER role_assignments (event-less; see header note) ──
        sb.AppendLine("-- ── Privileged LOCAL_HR / LOCAL_LEADER role_assignments (event-less; SQL-seeded because");
        sb.AppendLine("--    POST /api/admin/roles/grant has a pre-existing schema bug — see SPRINT-84) ──");
        sb.AppendLine("INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES");
        AppendRows(sb, dataset.PrivilegedRoles, (rb, r) => WriteRoleRow(rb, r));
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        // ── 6. Bulk EMPLOYEE role_assignments (event-less, matches baseline init.sql:894) ──
        sb.AppendLine("-- ── Bulk EMPLOYEE role_assignments (event-less by design; assigned_by='DEMO_SEED') ──");
        sb.AppendLine("INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES");
        AppendRows(sb, dataset.EmployeeRoles, (rb, r) => WriteRoleRow(rb, r));
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        sb.AppendLine("-- ============================================================================");
        sb.AppendLine("-- End of generated demo seed.");
        sb.AppendLine("-- ============================================================================");

        return sb.ToString();
    }

    private static string Header(DemoDataset dataset)
    {
        var m = dataset.Manifest;
        return
            "-- ============================================================================\n" +
            "-- 99-demo-seed.sql — OPT-IN realistic demo dataset (S84). GENERATED ARTIFACT.\n" +
            "-- Produced deterministically by tools/StatsTid.DemoSeed (fixed seed). Do not hand-edit.\n" +
            "-- Loaded ONLY via the demo compose overlay (docker/docker-compose.demo.yml), on a\n" +
            "-- FRESH postgres volume. The overlay mounts this as zz-demo-seed.sql so it sorts AFTER\n" +
            "-- init.sql (the entrypoint runs files in byte-lexical order; the on-disk 99- name would\n" +
            "-- sort BEFORE init.sql). NEVER mounted in CI.\n" +
            "-- The reporting TREES + activity are loaded post-boot via the StatsTid.DemoSeed API\n" +
            "-- loader (event-emitting). This file carries orgs + users + bulk EMPLOYEE role_assignments\n" +
            "-- + the privileged LOCAL_HR/LOCAL_LEADER rows (SQL-seeded, event-less — the roles/grant\n" +
            "-- API has a product defect; see SPRINT-84) + a demo GLOBAL_ADMIN bootstrap.\n" +
            "--\n" +
            $"-- scale={m.Scale}  seed={m.Seed}  referenceDate={m.ReferenceDate}\n" +
            $"-- orgs={dataset.Orgs.Count}  users={dataset.Users.Count} (+1 demo_admin)  " +
            $"employeeRoles={dataset.EmployeeRoles.Count}  privilegedRoles={dataset.PrivilegedRoles.Count}\n" +
            "-- ============================================================================\n\n";
    }

    /// <summary>Append a multi-row VALUES body: comma-separated rows, 4-space indent, one per line.</summary>
    private static void AppendRows<T>(StringBuilder sb, IReadOnlyList<T> rows, Action<StringBuilder, T> writeRow)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            sb.Append("    ");
            writeRow(sb, rows[i]);
            sb.AppendLine(i == rows.Count - 1 ? "" : ",");
        }
    }

    private static void WriteRoleRow(StringBuilder rb, DemoRoleRow r)
    {
        rb.Append('(')
          .Append(Lit(r.UserId)).Append(", ")
          .Append(Lit(r.RoleId)).Append(", ")
          .Append(r.OrgId is null ? "NULL" : Lit(r.OrgId)).Append(", ")
          .Append(Lit(r.ScopeType)).Append(", ")
          .Append(Lit("DEMO_SEED")).Append(')');
    }

    /// <summary>SQL single-quoted literal with quote-doubling. Invariant culture; no interpolation injection.</summary>
    private static string Lit(string s) => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";

    // Defensive: ensure any decimal formatting uses invariant culture (none currently emitted to SQL,
    // but kept for future numeric columns).
    internal static string Num(decimal d) => d.ToString(CultureInfo.InvariantCulture);
}
