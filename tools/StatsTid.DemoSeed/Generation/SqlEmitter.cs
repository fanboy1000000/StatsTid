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
/// S97 / TASK-9706 also emits the structured <c>enheder</c> + <c>user_enheder</c> tables
/// (ADR-035 display metadata, promoted from the per-user <c>enhed_label</c>): the DISTINCT
/// (organisation, name) labels with deterministic-per-run UUIDs + one membership tag per
/// labelled user. The <c>enhed_label</c> pre-seed stays as the transitional display fallback.
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
        sb.AppendLine("--    no AFDELING/TEAM org rows — the former leaf-unit names ride employee_profiles.enhed_label) ──");
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

        // ── 3b. Demo employee_profiles enhed_label pre-seed (S92 / ADR-035) ──
        //    Every rank-and-file demo user used to sit on an AFDELING/TEAM leaf org; the flatten
        //    moves them UP to their ORGANISATION and carries the former unit name as the
        //    display-only employee_profiles.enhed_label. We pre-seed ONE live profile row (+ a
        //    matching CREATED audit row) per such user so the label survives first boot. The
        //    EmployeeProfileSeeder is idempotent (it only creates rows for users lacking a live
        //    effective_to IS NULL row), so it SKIPS these and backfills the rest (the org-root
        //    managers, who carry no enhed_label) with a NULL-enhed_label profile as before.
        //    Display-only, inert for rules/payroll; no outbox event (a SQL-side outbox INSERT would
        //    bypass the EventSerializer registry — the EmployeeProfileSeeder's documented constraint).
        //    NOTE: this runs as zz-demo-seed.sql AFTER init.sql, which adds the enhed_label column.
        var enhedUsers = dataset.Users.Where(u => u.IsActive && u.EnhedLabel is not null).ToList();
        if (enhedUsers.Count > 0)
        {
            sb.AppendLine("-- ── Demo employee_profiles enhed_label pre-seed (former AFDELING/TEAM unit name, display-only) ──");
            sb.AppendLine("INSERT INTO employee_profiles (employee_id, part_time_fraction, position, enhed_label)");
            sb.AppendLine("SELECT m.employee_id, 1.000, NULL, m.enhed_label");
            sb.AppendLine("FROM (VALUES");
            AppendRows(sb, enhedUsers, (rb, u) =>
                rb.Append('(').Append(Lit(u.UserId)).Append(", ").Append(Lit(u.EnhedLabel!)).Append(')'));
            sb.AppendLine(") AS m(employee_id, enhed_label)");
            sb.AppendLine("WHERE NOT EXISTS (");
            sb.AppendLine("    SELECT 1 FROM employee_profiles p");
            sb.AppendLine("    WHERE p.employee_id = m.employee_id AND p.effective_to IS NULL");
            sb.AppendLine(");");
            sb.AppendLine();

            sb.AppendLine("INSERT INTO employee_profile_audit (");
            sb.AppendLine("    profile_id, employee_id, action,");
            sb.AppendLine("    previous_data, new_data,");
            sb.AppendLine("    version_before, version_after,");
            sb.AppendLine("    actor_id, actor_role)");
            sb.AppendLine("SELECT p.profile_id, p.employee_id, 'CREATED',");
            sb.AppendLine("       NULL,");
            sb.AppendLine("       jsonb_build_object('partTimeFraction', 1.000, 'position', NULL, 'enhedLabel', p.enhed_label),");
            sb.AppendLine("       NULL, 1,");
            sb.AppendLine("       'DEMO_SEED', 'SYSTEM'");
            sb.AppendLine("FROM employee_profiles p");
            sb.AppendLine("WHERE p.employee_id LIKE 'demo\\_%'");
            sb.AppendLine("  AND p.effective_to IS NULL");
            sb.AppendLine("  AND p.enhed_label IS NOT NULL");
            sb.AppendLine("  AND NOT EXISTS (");
            sb.AppendLine("      SELECT 1 FROM employee_profile_audit a");
            sb.AppendLine("      WHERE a.profile_id = p.profile_id AND a.action = 'CREATED'");
            sb.AppendLine("  );");
            sb.AppendLine();
        }

        // ── 3c. S97 / TASK-9706 — structured enheder + user_enheder tags (ADR-035) ──
        //     Promote the per-user enhed_label (above) into the S97 structured model: the DISTINCT
        //     (organisation_id, name) labels become enheder rows (deterministic-per-run UUID id),
        //     and each labelled user gets a user_enheder tag (label → their org's matching enhed).
        //     PURE DISPLAY METADATA — zero authority/scope/approval meaning. The enhed_label pre-seed
        //     above STAYS (the transitional read-only display fallback). The DISTINCT set satisfies the
        //     partial-unique (organisation_id, lower(name)) WHERE deleted_at IS NULL; the
        //     user_enheder FK resolves to the row emitted just above. ON CONFLICT DO NOTHING (re-run-safe;
        //     a fresh volume is empty so nothing is dropped). Runs as zz-demo-seed.sql AFTER init.sql,
        //     which creates the enheder / user_enheder tables.
        if (dataset.Enheder.Count > 0)
        {
            sb.AppendLine("-- ── Demo enheder (DISTINCT former-unit display names per Organisation; ADR-035 metadata) ──");
            sb.AppendLine("INSERT INTO enheder (enhed_id, organisation_id, name) VALUES");
            AppendRows(sb, dataset.Enheder, (rb, e) =>
                rb.Append('(')
                  .Append("UUID ").Append(Lit(e.EnhedId)).Append(", ")
                  .Append(Lit(e.OrganisationId)).Append(", ")
                  .Append(Lit(e.Name)).Append(')'));
            sb.AppendLine("ON CONFLICT DO NOTHING;");
            sb.AppendLine();

            sb.AppendLine("-- ── Demo user_enheder tags (one per labelled user; same-Organisation invariant) ──");
            sb.AppendLine("INSERT INTO user_enheder (user_id, enhed_id) VALUES");
            AppendRows(sb, dataset.UserEnheder, (rb, t) =>
                rb.Append('(')
                  .Append(Lit(t.UserId)).Append(", ")
                  .Append("UUID ").Append(Lit(t.EnhedId)).Append(')'));
            sb.AppendLine("ON CONFLICT DO NOTHING;");
            sb.AppendLine();
        }

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
            $"-- enheder={dataset.Enheder.Count}  userEnheder={dataset.UserEnheder.Count} (S97 / ADR-035 structured Enhed metadata)\n" +
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
