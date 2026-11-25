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
/// S127 / TASK-12701a: a per-org PROJECT catalogue is emitted too (every org, MAO included). It is
/// SQL-seeded rather than loaded via the admin API for the same reason the org/user rows are —
/// projects are structural reference data the loader's activity stage already depends on: the
/// allocations it posts name project CODES, so the rows must exist before the first skema save.
///
/// ADR-043's config stage: after the projects, three more sections in a fixed order and shape
/// (comment lines, one INSERT line, the rows, <c>ON CONFLICT DO NOTHING;</c>, one blank line) —
/// local agreement profiles, customary days and HR facts (<see cref="ConfigStageSeed"/>). They are
/// starting state, event-less with no audit row like every row here; the first edit made in the
/// product writes the first audit row and event, with the seeded row as its predecessor. Each
/// section head says why it changes no pay.
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
    internal const string DemoAdminId = "demo_admin";

    /// <summary>demo_admin's agreement and category: its users row, and the input of its HR-facts
    /// row (<see cref="ConfigStageSeed.HrFacts"/>), so the two cannot drift apart.</summary>
    internal const string DemoAdminAgreementCode = "AC";
    internal const string DemoAdminEmploymentCategory = "Kontorchef";

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
          .Append(Lit(DemoAdminAgreementCode)).Append(", ")
          .Append(Lit("OK24")).Append(", ")
          .Append(Lit(DemoAdminEmploymentCategory)).Append(", ")
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

        // ── 7. S127 / TASK-12701a — the per-org project catalogue ──
        sb.AppendLine("-- ── Projects, one catalogue per org INCLUDING the MAO root (S127 / TASK-12701a; owner");
        sb.AppendLine("--    ruling B). Projects are strictly ORG-SCOPED, so an employee in a project-less org has");
        sb.AppendLine("--    an EMPTY project set — not a filtered one — and could never allocate their hours, which");
        sb.AppendLine("--    the S127 submit-time gate makes a hard precondition for sending a month. demo_admin");
        sb.AppendLine("--    homes on the MAO, which is why the MAO is in here too. project_id is omitted");
        sb.AppendLine("--    deliberately: the column defaults to gen_random_uuid(), nothing references a project by");
        sb.AppendLine("--    id (allocations key on project_code), and omitting it keeps this artifact byte-stable. ──");
        sb.AppendLine("INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by) VALUES");
        AppendRows(sb, dataset.Projects, (rb, p) =>
        {
            rb.Append('(')
              .Append(Lit(p.OrgId)).Append(", ")
              .Append(Lit(p.ProjectCode)).Append(", ")
              .Append(Lit(p.ProjectName)).Append(", ")
              .Append(p.SortOrder.ToString(CultureInfo.InvariantCulture)).Append(", ")
              .Append(Lit("DEMO_SEED")).Append(')');
        });
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        // ── 8. Local agreement profiles (ADR-043 D6) ──
        sb.AppendLine("-- ── Local agreement profiles (ADR-043 D6): one per org × agreement (AC, HK, PROSA) × OK version");
        sb.AppendLine("--    (OK24, OK26), setting only the flex plus cap, 37 h (as SDU and DTU, ADR-043 D6). No payslip");
        sb.AppendLine("--    moves: the four legacy columns that reach pay (weekly norm, flex cap, overtime ceiling,");
        sb.AppendLine("--    pre-approval) are written out as NULL, so the config lookup keeps the central values; nothing");
        sb.AppendLine("--    in pay reads the plus cap yet (its first reader decides its effect); and every row starts at");
        sb.AppendLine("--    the history anchor 0001-01-01, so no profile starts inside a month the demo calculates and");
        sb.AppendLine("--    every month stays whole. ──");
        sb.AppendLine("INSERT INTO local_agreement_profiles (org_id, agreement_code, ok_version, effective_from, weekly_norm_hours, max_flex_balance, max_overtime_hours_per_period, overtime_requires_pre_approval, flex_plus_cap_hours, created_by) VALUES");
        AppendRows(sb, dataset.LocalProfiles, (rb, p) =>
        {
            rb.Append('(')
              .Append(Lit(p.OrgId)).Append(", ")
              .Append(Lit(p.AgreementCode)).Append(", ")
              .Append(Lit(p.OkVersion)).Append(", ")
              .Append("DATE ").Append(Lit(p.EffectiveFrom)).Append(", ")
              .Append("NULL, NULL, NULL, NULL, ")
              .Append(Num(p.FlexPlusCapHours)).Append(", ")
              .Append(Lit("DEMO_SEED")).Append(')');
        });
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        // ── 9. Customary days (ADR-043 D9) ──
        sb.AppendLine("-- ── Customary days (ADR-043 D9): three per org, each on its own day: 5 June and 24 December every");
        sb.AppendLine("--    year as a paid day off given to all, and the Friday after Ascension Day as a closure day;");
        sb.AppendLine("--    whole days, every agreement, valid from 1 January of the activity month's year. custom_id is");
        sb.AppendLine("--    md5('demo-custom|<org>|<day>')::uuid, a pure function of the org and the day, so re-applying");
        sb.AppendLine("--    this file adds no second custom (the table has no other unique key). No payslip moves:");
        sb.AppendLine("--    nothing reads customs yet; the first reader decides how a custom shapes hours and pay (the");
        sb.AppendLine("--    generated hours do not honour them). ──");
        sb.AppendLine("INSERT INTO customary_days (custom_id, org_id, agreement_code, recurring_month, recurring_day, single_date, kind, part, paid_mode, valid_from, created_by) VALUES");
        AppendRows(sb, dataset.CustomaryDays, (rb, c) =>
        {
            var day = c.SingleDate ?? string.Create(CultureInfo.InvariantCulture, $"{c.RecurringMonth:00}-{c.RecurringDay:00}");
            rb.Append("(md5(").Append(Lit($"demo-custom|{c.OrgId}|{day}")).Append(")::uuid, ")
              .Append(Lit(c.OrgId)).Append(", ")
              .Append("NULL, ")
              .Append(c.RecurringMonth is { } m ? m.ToString(CultureInfo.InvariantCulture) : "NULL").Append(", ")
              .Append(c.RecurringDay is { } d ? d.ToString(CultureInfo.InvariantCulture) : "NULL").Append(", ")
              .Append(c.SingleDate is null ? "NULL" : "DATE " + Lit(c.SingleDate)).Append(", ")
              .Append(Lit(c.Kind)).Append(", ")
              .Append(Lit(c.Part)).Append(", ")
              .Append(c.PaidMode is null ? "NULL" : Lit(c.PaidMode)).Append(", ")
              .Append("DATE ").Append(Lit(c.ValidFrom)).Append(", ")
              .Append(Lit("DEMO_SEED")).Append(')');
        });
        sb.AppendLine("ON CONFLICT DO NOTHING;");
        sb.AppendLine();

        // ── 10. Employee HR facts (ADR-043 D4) ──
        sb.AppendLine("-- ── Employee HR facts (ADR-043 D4): one row per user this file inserts, demo_admin first, leavers");
        sb.AppendLine("--    included; on the flex scheme, from the history anchor 0001-01-01. The overtime regime: HK and");
        sb.AppendLine("--    PROSA OVERTIME; AC by employment category, copying init.sql's role_config_overrides rows");
        sb.AppendLine("--    (ADR-024; the demo spells Fuldmaegtig in ASCII): Fuldmaegtig CONTRACTUAL, Specialkonsulent");
        sb.AppendLine("--    DISCRETIONARY, Chefkonsulent and Kontorchef NONE; NULL (\"missing\" in the editor) for Standard.");
        sb.AppendLine("--    The values are provisional (ADR-043 provisional item 5); everyone on flex is the interim limit");
        sb.AppendLine("--    R-17 (QUAL-BF-010). No payslip moves: nothing reads these facts yet. ──");
        sb.AppendLine("INSERT INTO employee_hr_facts (employee_id, overtime_regime, on_flex_scheme, effective_from) VALUES");
        AppendRows(sb, dataset.HrFacts, (rb, f) =>
        {
            rb.Append('(')
              .Append(Lit(f.EmployeeId)).Append(", ")
              .Append(f.OvertimeRegime is null ? "NULL" : Lit(f.OvertimeRegime)).Append(", ")
              .Append(f.OnFlexScheme ? "TRUE" : "FALSE").Append(", ")
              .Append("DATE ").Append(Lit(f.EffectiveFrom)).Append(')');
        });
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
            $"employeeRoles={dataset.EmployeeRoles.Count}  privilegedRoles={dataset.PrivilegedRoles.Count}  " +
            $"projects={dataset.Projects.Count}\n" +
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

    // Invariant culture, so a decimal never takes a locale's comma (the profiles' plus cap).
    internal static string Num(decimal d) => d.ToString(CultureInfo.InvariantCulture);
}
