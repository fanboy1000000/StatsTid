using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tools.DemoSeed.Generation;

/// <summary>
/// S84 / TASK-8401 — the deterministic demo-data generator. Given a scale + a fixed reference
/// date, it produces a fully-determined <see cref="DemoDataset"/> (orgs, users, bulk EMPLOYEE
/// roles, and the manifest). Same (seed, scale, referenceDate) ⇒ byte-identical output.
///
/// Determinism contract:
///   - ONE seeded <see cref="Random"/> (default seed 42), consumed in a FIXED order.
///   - No wall-clock: all dates derive from <see cref="_referenceDate"/>.
///   - Stable ids: org/user ids are positional (demo_styx1_0007 …) so ordering is fixed.
///
/// Structural realism (S92 / ADR-035 flatten): the org tree is 2 LEVELS — MAO (root) →
/// ORGANISATION (the smallest authority unit). The former AFDELING/TEAM leaf orgs are gone;
/// every user sits directly on their Organisation and carries the former leaf-unit name as a
/// display-only <c>employee_profiles.enhed_label</c>. The REPORTING tree keeps its realistic
/// depth (span ~TargetSpan, 12–18% managers, EXACTLY ONE root per Organisation, NO cycles —
/// the manager of an employee is always strictly closer to the root); reporting depth is a
/// people-graph property, independent of the now-flat ORG graph.
/// </summary>
public sealed class DemoGenerator
{
    // The baseline init.sql bcrypt hash for the dev password "password".
    public const string PasswordHash = "$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te";
    public const string Password = "password";

    // Baseline id set (init.sql) the demo set MUST be disjoint from.
    private static readonly HashSet<string> BaselineOrgIds = new(StringComparer.Ordinal)
    {
        "MIN01", "MIN02", "STY01", "STY02", "STY03", "STY04", "STY05",
        "AFD01", "AFD02", "AFD03", "AFD04", "AFD05",
    };

    private static readonly HashSet<string> BaselineUserIds = new(StringComparer.Ordinal)
    {
        "admin01", "admin02", "ladm01", "ladm02", "hr01", "hr02",
        "mgr01", "mgr02", "mgr03",
        "emp001", "emp002", "emp003", "emp004", "emp005",
        "emp006", "emp007", "emp008", "emp009", "emp010",
    };

    private readonly ScaleConfig _config;
    private readonly int _seed;
    private readonly Random _rng;
    private readonly DateOnly _referenceDate;

    public DemoGenerator(string scale, int seed, DateOnly referenceDate)
    {
        _config = ScaleConfig.For(scale);
        _seed = seed;
        _rng = new Random(seed);
        _referenceDate = referenceDate;
    }

    public DemoDataset Generate()
    {
        var orgs = new List<DemoOrg>();
        var users = new List<DemoUser>();
        var employeeRoles = new List<DemoRoleRow>();
        var privilegedRoles = new List<DemoRoleRow>();

        var manifest = new DemoManifest
        {
            Scale = _config.Name,
            Seed = _seed,
            ReferenceDate = _referenceDate.ToString("yyyy-MM-dd"),
            AdminUserId = "demo_admin",
            Password = Password,
        };

        // 1. MAO root (Ministeransvarsområde — the former MINISTRY). Orgs only, no users:
        //    every user sits on an ORGANISATION under this MAO (S92 / ADR-035 flatten).
        var mao = new DemoOrg
        {
            OrgId = _config.MinistryId,
            OrgName = _config.MinistryName,
            OrgType = "MAO",
            ParentOrgId = null,
            MaterializedPath = $"/{_config.MinistryId}/",
            AgreementCode = "AC",
            OkVersion = "OK24",
            OrganisationId = _config.MinistryId,
            Depth = 0,
        };
        orgs.Add(mao);

        // 2. Per-tree generation (one ORGANISATION per profile, under the MAO).
        foreach (var profile in _config.Trees)
        {
            GenerateTree(profile, mao, orgs, users, employeeRoles, privilegedRoles, manifest);
        }

        // 3. Activity / part-time / vikar / messy plans (need the full user list).
        GenerateProfileEdits(users, manifest);
        GenerateActivity(users, manifest);
        GenerateVikars(users, manifest);
        GenerateMessyCases(users, manifest);

        // 4. Disjointness assertion (NOTE-1 — ON CONFLICT silently drops collisions).
        AssertDisjointFromBaseline(orgs, users, manifest);

        return new DemoDataset
        {
            Orgs = orgs,
            Users = users,
            EmployeeRoles = employeeRoles,
            PrivilegedRoles = privilegedRoles,
            Manifest = manifest,
        };
    }

    private void GenerateTree(
        TreeProfile profile,
        DemoOrg mao,
        List<DemoOrg> orgs,
        List<DemoUser> users,
        List<DemoRoleRow> employeeRoles,
        List<DemoRoleRow> privilegedRoles,
        DemoManifest manifest)
    {
        var root = profile.OrganisationId;
        var lower = root.ToLowerInvariant();

        // ── 2a. Org hierarchy (S92 / ADR-035 flatten): ONE ORGANISATION (depth 1) under the
        //        MAO (depth 0). NO AFDELING/TEAM org rows — the former leaf-unit names become
        //        display-only enhed_label metadata on the users (see the Enhed pool below).
        //        The Organisation is BOTH the user-home org AND the reporting-tree root. ──
        var organisation = new DemoOrg
        {
            OrgId = root,
            OrgName = profile.OrgName,
            OrgType = "ORGANISATION",
            ParentOrgId = mao.OrgId,
            MaterializedPath = $"/{mao.OrgId}/{root}/",
            AgreementCode = profile.RootAgreement,
            OkVersion = "OK24",
            OrganisationId = root,
            Depth = 1,
        };
        orgs.Add(organisation);

        var treeOrgs = new List<DemoOrg> { organisation };

        // The set of Enhed (former leaf-unit) display labels this Organisation's rank-and-file
        // are distributed across. Sized like the old leaf-org fan-out (~TargetSpan users per
        // enhed) so the demo keeps realistic per-unit grouping — purely as a display label now.
        var leafTarget = Math.Max(1, profile.TargetUsers / Math.Max(1, _config.TargetSpan));
        var enhedCount = Math.Clamp(leafTarget, 1, DanishPools.EnhedFragments.Length);
        var enheder = new List<string>(enhedCount);
        for (var e = 0; e < enhedCount; e++)
            enheder.Add(DanishPools.EnhedFragments[e % DanishPools.EnhedFragments.Length]);

        // ── 2b. Users. EVERY user sits directly on the ORGANISATION (primary_org = the
        //        Organisation, tree_root = the Organisation). The top manager (the SINGLE
        //        reporting-tree root) sits on the Organisation with NO enhed_label; the
        //        rank-and-file carry a round-robin enhed_label (their former leaf unit). ──
        int userSeq = 0;
        string NextUserId() => $"demo_{lower}_{(++userSeq):D4}";

        var treeUsers = new List<DemoUser>();

        // The top manager (reporting-tree root). primary_org = the Organisation; no enhed_label
        // (it sits at the Organisation level, not in a sub-unit).
        var topManager = MakeUser(NextUserId(), organisation.OrgId, root, profile, isSenior: true, leaverAllowed: false, enhedLabel: null);
        topManager.IsManager = true;
        users.Add(topManager);
        treeUsers.Add(topManager);

        // Remaining users: all on the Organisation, round-robin across the Enhed labels
        // (deterministic — same index walk as the old leaf-org distribution).
        var remaining = profile.TargetUsers - 1;
        for (var i = 0; i < remaining; i++)
        {
            var enhed = enheder[i % enheder.Count];
            var u = MakeUser(NextUserId(), organisation.OrgId, root, profile, isSenior: false, leaverAllowed: true, enhedLabel: enhed);
            users.Add(u);
            treeUsers.Add(u);
        }

        // ── 2c. Reporting edges. Build a layered tree over the ACTIVE users only (the import API
        //        rejects inactive employees/managers — leavers stay in the dataset + the bulk
        //        EMPLOYEE roles, but are NOT part of the active reporting tree). The top manager is
        //        the root (never a leaver); managers/leaves are picked from the active set so every
        //        edge points strictly closer to the root (NO cycles by construction). ──
        var activeTreeUsers = treeUsers.Where(u => u.IsActive).ToList();
        BuildReportingTree(profile, activeTreeUsers, manifest);

        // ── 2d. Bulk EMPLOYEE role rows (SQL-seeded, event-less). EVERY tree user (active + leaver). ──
        foreach (var u in treeUsers)
            employeeRoles.Add(new DemoRoleRow { UserId = u.UserId, RoleId = "EMPLOYEE", OrgId = u.PrimaryOrgId, ScopeType = "ORG_ONLY" });

        // ── 2e. Privileged roles: one LOCAL_HR at the ORGANISATION root + a LOCAL_LEADER for every
        //        ACTIVE manager (so the dashboards/approvals/vikar work). SQL-SEEDED (event-less)
        //        because the live POST /api/admin/roles/grant endpoint has a pre-existing schema
        //        bug (its role_assignment_audit INSERT targets non-existent columns) — see SPRINT-84.
        //        Authorization reads role_assignments table-direct, so these rows yield working
        //        scopes; the API path remains a documented follow-up. The manifest's RoleGrants list
        //        is left EMPTY (the loader has nothing to grant). ──
        privilegedRoles.Add(new DemoRoleRow
        {
            UserId = topManager.UserId, RoleId = "LOCAL_HR", OrgId = root, ScopeType = "ORG_ONLY",
        });
        foreach (var m in treeUsers.Where(u => u.IsManager && u.IsActive))
        {
            privilegedRoles.Add(new DemoRoleRow
            {
                UserId = m.UserId, RoleId = "LOCAL_LEADER", OrgId = m.PrimaryOrgId, ScopeType = "ORG_ONLY",
            });
        }

        var managerCount = treeUsers.Count(u => u.IsManager);
        manifest.Trees.Add(new DemoTree
        {
            OrganisationId = root,
            RootEmployeeId = topManager.UserId,
            OrgCount = treeOrgs.Count,
            UserCount = treeUsers.Count,
            ManagerCount = managerCount,
            MaxDepth = treeOrgs.Max(o => o.Depth),
        });
    }

    /// <summary>
    /// Reporting-tree builder with EXPLICIT manager-count control. Guarantees: exactly one root
    /// (the top manager, no outgoing edge), no cycles (every edge points strictly closer to the
    /// root — managers form a layered spine, leaves hang off managers), span ≈ TargetSpan,
    /// ~14% managers (12–18% band), depth 3–5 reporting layers.
    ///
    /// Construction:
    ///   1. Designate exactly M = round(N * 0.14) managers (incl. the root, index 0).
    ///   2. Lay the M managers into a balanced spine (root → mid-managers → … by TargetSpan),
    ///      emitting a PRIMARY edge per non-root manager to its parent manager.
    ///   3. Distribute the remaining N − M leaf employees round-robin across ALL managers,
    ///      emitting a PRIMARY edge per leaf to its manager.
    /// Because every leaf's manager is a spine node and every spine edge points up, no cycle
    /// can form and there is exactly one root (the only node with no outgoing edge).
    /// </summary>
    private void BuildReportingTree(TreeProfile profile, List<DemoUser> treeUsers, DemoManifest manifest)
    {
        var span = _config.TargetSpan;
        var effFrom = _referenceDate.AddMonths(-6).ToString("yyyy-MM-dd");
        var n = treeUsers.Count;

        // 1. Manager count ≈ 14% of headcount (at least the root + a couple for small trees).
        var managerCount = Math.Max(2, (int)Math.Round(n * 0.14));
        managerCount = Math.Min(managerCount, n - 1); // always leave ≥1 leaf
        var managers = treeUsers.Take(managerCount).ToList();
        var leaves = treeUsers.Skip(managerCount).ToList();

        foreach (var m in managers)
            m.IsManager = true;

        void AddEdge(DemoUser emp, DemoUser mgr) => manifest.ReportingEdges.Add(new DemoReportingEdge
        {
            EmployeeId = emp.UserId,
            ManagerId = mgr.UserId,
            OrganisationId = profile.OrganisationId,
            Relationship = "PRIMARY",
            EffectiveFrom = effFrom,
        });

        // 2. Spine: managers[0] is the root; lay the rest into BFS layers under it (≤ span children
        //    per manager). This is a pure index walk (deterministic). manager i (i≥1) reports to
        //    manager (i-1)/span — a strictly smaller index ⇒ always closer to the root ⇒ no cycle.
        for (var i = 1; i < managers.Count; i++)
        {
            var parentIndex = (i - 1) / span;
            AddEdge(managers[i], managers[parentIndex]);
        }

        // 3. Leaves: round-robin across ALL managers (balanced load; the root also carries a few).
        for (var i = 0; i < leaves.Count; i++)
        {
            var mgr = managers[i % managers.Count];
            AddEdge(leaves[i], mgr);
        }
    }

    private DemoUser MakeUser(string userId, string orgId, string treeRoot, TreeProfile profile, bool isSenior, bool leaverAllowed, string? enhedLabel)
    {
        var first = DanishPools.FirstNames[_rng.Next(DanishPools.FirstNames.Length)];
        var last = DanishPools.LastNames[_rng.Next(DanishPools.LastNames.Length)];
        var displayName = $"{first} {last}";
        var email = $"{DanishPools.ToAsciiSlug(first)}.{DanishPools.ToAsciiSlug(last)}.{userId}@demo.dk";

        var agreement = DrawAgreement(profile.AgreementMix);

        // Most OK24; ~8% OK26 (so the version surfaces show variety).
        var okVersion = _rng.NextDouble() < 0.08 ? "OK26" : "OK24";

        // Employment category: seniors get a chef title; otherwise spread.
        var category = isSenior
            ? "Kontorchef"
            : DanishPools.EmploymentCategories[_rng.Next(DanishPools.EmploymentCategories.Length)];

        // Age spread: 25–67; ~12% are 60+ (senior-day surfaces). Seniors skew older.
        int age = isSenior ? 50 + _rng.Next(16) : (_rng.NextDouble() < 0.12 ? 60 + _rng.Next(8) : 25 + _rng.Next(35));
        var birth = _referenceDate.AddYears(-age).AddDays(_rng.Next(0, 365));

        // Tenure spread: 0–30 years.
        int tenureYears = _rng.Next(0, 31);
        var startDate = _referenceDate.AddYears(-tenureYears).AddDays(-_rng.Next(0, 365));
        if (startDate > _referenceDate)
            startDate = _referenceDate;

        // A few leavers (~3%): employment_end_date in the recent past; still a row, inactive.
        string? endDate = null;
        bool isActive = true;
        if (leaverAllowed && _rng.NextDouble() < 0.03)
        {
            endDate = _referenceDate.AddDays(-_rng.Next(10, 300)).ToString("yyyy-MM-dd");
            isActive = false;
        }

        return new DemoUser
        {
            UserId = userId,
            Username = userId,
            PasswordHash = PasswordHash,
            DisplayName = displayName,
            Email = email,
            PrimaryOrgId = orgId,
            AgreementCode = agreement,
            OkVersion = okVersion,
            EmploymentCategory = category,
            BirthDate = birth.ToString("yyyy-MM-dd"),
            EmploymentStartDate = startDate.ToString("yyyy-MM-dd"),
            EmploymentEndDate = endDate,
            IsActive = isActive,
            OrganisationId = treeRoot,
            EnhedLabel = enhedLabel,
        };
    }

    private string DrawAgreement((int Ac, int Hk, int Prosa) mix)
    {
        var total = mix.Ac + mix.Hk + mix.Prosa;
        var roll = _rng.Next(total);
        if (roll < mix.Ac) return "AC";
        if (roll < mix.Ac + mix.Hk) return "HK";
        return "PROSA";
    }

    // ── Part-time / position subset (~PartTimeFraction of ACTIVE users) ──
    private void GenerateProfileEdits(List<DemoUser> users, DemoManifest manifest)
    {
        var active = users.Where(u => u.IsActive).ToList();
        foreach (var u in active)
        {
            if (_rng.NextDouble() >= _config.PartTimeFraction)
                continue;
            // Odd-but-valid fractions in [0.300, 1.000] (NUMERIC(4,3)).
            var fraction = Math.Round(0.300m + (decimal)_rng.NextDouble() * 0.700m, 3);
            var position = DanishPools.Positions[_rng.Next(DanishPools.Positions.Length)];
            manifest.ProfileEdits.Add(new DemoProfileEdit
            {
                EmployeeId = u.UserId,
                PartTimeFraction = fraction,
                Position = position,
                // S92 / ADR-035 — carry the user's enhed_label so the profile PUT (which
                // supersedes the full live row) preserves the SQL-pre-seeded display label.
                EnhedLabel = u.EnhedLabel,
            });
        }
    }

    // ── Activity (~ActivityFraction of ACTIVE users) ──
    private void GenerateActivity(List<DemoUser> users, DemoManifest manifest)
    {
        var active = users.Where(u => u.IsActive).ToList();
        // Use a recent COMPLETE month relative to the reference date (avoids the current/boundary month).
        var activityMonth = _referenceDate.AddMonths(-1);
        var year = activityMonth.Year;
        var month = activityMonth.Month;
        string[] outcomes = { "NONE", "SUBMITTED", "APPROVED", "REJECTED" };

        foreach (var u in active)
        {
            if (_rng.NextDouble() >= _config.ActivityFraction)
                continue;

            var absences = new List<DemoAbsence>();
            // ONE dominant absence type per employee's month, with a quota-safe day count:
            //   VACATION  → 1–3 days (well under the annual quota),
            //   SICK_DAY  → 1–2 days (no fixed quota),
            //   CARE_DAY  → exactly 1 day (the CARE_DAY quota is small — 2 — so 1 stays safe even
            //               if the employee already consumed elsewhere).
            var type = _rng.Next(3) switch
            {
                0 => "VACATION",
                1 => "SICK_DAY",
                _ => "CARE_DAY",
            };
            var count = type switch
            {
                "VACATION" => 1 + _rng.Next(3),
                "SICK_DAY" => 1 + _rng.Next(2),
                _ => 1, // CARE_DAY
            };
            var usedDays = new HashSet<int>();
            for (var i = 0; i < count; i++)
            {
                var day = PickNonBoundaryWeekday(year, month, usedDays);
                if (day == 0) break;
                usedDays.Add(day);
                absences.Add(new DemoAbsence
                {
                    Date = new DateOnly(year, month, day).ToString("yyyy-MM-dd"),
                    AbsenceType = type,
                    Hours = 7.4m, // a full standard day (37h / 5)
                });
            }

            var outcome = outcomes[_rng.Next(outcomes.Length)];
            manifest.Activity.Add(new DemoActivity
            {
                EmployeeId = u.UserId,
                OrgId = u.PrimaryOrgId,
                AgreementCode = u.AgreementCode,
                OkVersion = u.OkVersion,
                Year = year,
                Month = month,
                Absences = absences,
                PeriodOutcome = outcome,
            });
        }
    }

    /// <summary>Pick a weekday (Mon–Fri) that is not the 1st or last day of the month, not already used.</summary>
    private int PickNonBoundaryWeekday(int year, int month, HashSet<int> used)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var day = 2 + _rng.Next(daysInMonth - 2); // [2 .. daysInMonth-1]
            if (used.Contains(day)) continue;
            var dow = new DateOnly(year, month, day).DayOfWeek;
            if (dow is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            return day;
        }
        return 0;
    }

    // ── Vikars: one per tree. The ABSENT manager is a MID-LEVEL manager (a manager that itself
    //    reports to someone); the VIKAR is the TREE-ROOT top manager, who holds LOCAL_HR at the
    //    Organisation (post-S92 flatten the Organisation IS the whole tree) with ORG_ONLY scope —
    //    covering EVERY user (all sit directly on the Organisation), hence ALL of the mid-manager's
    //    reports (the vikar-coverage census requires the stand-in's scope to cover every report's
    //    org). All demo users share the single Organisation org, so the LOCAL_HR ORG_ONLY scope at
    //    the Organisation covers the whole tree. ──
    private void GenerateVikars(List<DemoUser> users, DemoManifest manifest)
    {
        var effectiveTo = _referenceDate.AddMonths(2).ToString("yyyy-MM-dd");
        string[] reasons = { "FERIE", "SYGDOM", "ORLOV", "TJENESTEREJSE" };
        foreach (var tree in manifest.Trees)
        {
            // A mid-level manager = an employee that has BOTH an outgoing edge (reports to someone)
            // AND is itself a manager (appears as a manager_id). Pick the first such, deterministically.
            var managerIds = manifest.ReportingEdges
                .Where(e => e.OrganisationId == tree.OrganisationId)
                .Select(e => e.ManagerId).ToHashSet();
            var midManager = manifest.ReportingEdges
                .Where(e => e.OrganisationId == tree.OrganisationId
                            && e.ManagerId == tree.RootEmployeeId          // reports directly to root
                            && managerIds.Contains(e.EmployeeId))           // and is itself a manager
                .Select(e => e.EmployeeId)
                .FirstOrDefault();
            if (midManager is null || midManager == tree.RootEmployeeId) continue;

            manifest.Vikars.Add(new DemoVikar
            {
                ManagerId = midManager,                  // the absent mid-level manager
                VikarUserId = tree.RootEmployeeId,       // the tree-root top manager (LOCAL_HR, tree-wide)
                EffectiveTo = effectiveTo,
                Reason = reasons[_rng.Next(reasons.Length)],
            });
        }
    }

    // ── Messy cases: hand-curated scenarios spread across active users ──
    private void GenerateMessyCases(List<DemoUser> users, DemoManifest manifest)
    {
        var active = users.Where(u => u.IsActive).ToList();
        // Pick a deterministic, distinct set of users not already heavily used.
        var pool = new Queue<DemoUser>(active);
        string[] kinds = { "OK_TRANSITION", "AGREEMENT_CHANGE", "CROSS_STYRELSE_TRANSFER", "ODD_PART_TIME", "TERMINATED_THEN_REHIRED" };

        var leavers = users.Where(u => !u.IsActive).Take(_config.MessyCaseCount / 6 + 1).ToList();

        for (var i = 0; i < _config.MessyCaseCount; i++)
        {
            var kind = kinds[i % kinds.Length];
            if (kind == "TERMINATED_THEN_REHIRED")
            {
                var leaver = leavers.Count > 0 ? leavers[i % leavers.Count] : (pool.Count > 0 ? pool.Dequeue() : active[i % active.Count]);
                manifest.MessyCases.Add(new DemoMessyCase
                {
                    Kind = kind,
                    EmployeeId = leaver.UserId,
                    Note = "Leaver retained as a terminated-then-rehired candidate (employment_end_date set; rehire is a manual follow-up).",
                    Value = leaver.EmploymentEndDate,
                });
                continue;
            }

            if (pool.Count == 0) break;
            var u = pool.Dequeue();
            switch (kind)
            {
                case "OK_TRANSITION":
                    manifest.MessyCases.Add(new DemoMessyCase
                    {
                        Kind = kind, EmployeeId = u.UserId,
                        Note = "OK24 user flagged for OK24->OK26 transition exercise (version-correctness surface).",
                        Value = "OK26",
                    });
                    break;
                case "AGREEMENT_CHANGE":
                    var newAgr = u.AgreementCode == "AC" ? "HK" : "AC";
                    manifest.MessyCases.Add(new DemoMessyCase
                    {
                        Kind = kind, EmployeeId = u.UserId,
                        Note = $"Agreement-change candidate ({u.AgreementCode}->{newAgr}); exercises user_agreement_codes supersession.",
                        Value = newAgr,
                    });
                    break;
                case "CROSS_STYRELSE_TRANSFER":
                    // Related = the top manager of a DIFFERENT Organisation tree (post-S92 flatten
                    // the tree root IS the Organisation; "cross-styrelse" == cross-Organisation).
                    var otherTree = manifest.Trees.FirstOrDefault(t => t.OrganisationId != u.OrganisationId);
                    manifest.MessyCases.Add(new DemoMessyCase
                    {
                        Kind = kind, EmployeeId = u.UserId,
                        Note = "Cross-Organisation transfer candidate (a stale-key drift / edge-auth surface).",
                        RelatedId = otherTree?.RootEmployeeId,
                        Value = otherTree?.OrganisationId,
                    });
                    break;
                case "ODD_PART_TIME":
                    var oddFraction = (0.333m + (decimal)i / 100m);
                    if (oddFraction >= 1.0m) oddFraction = 0.625m;
                    manifest.MessyCases.Add(new DemoMessyCase
                    {
                        Kind = kind, EmployeeId = u.UserId,
                        Note = "Odd part-time fraction candidate (pro-rated accrual surface).",
                        Value = Math.Round(oddFraction, 3).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    });
                    break;
            }
        }
    }

    private static void AssertDisjointFromBaseline(List<DemoOrg> orgs, List<DemoUser> users, DemoManifest manifest)
    {
        foreach (var o in orgs)
            if (BaselineOrgIds.Contains(o.OrgId))
                throw new InvalidOperationException(
                    $"Demo org id '{o.OrgId}' collides with a baseline init.sql org id. " +
                    "ON CONFLICT DO NOTHING would silently drop it, malforming the tree. FAIL.");

        foreach (var u in users)
            if (BaselineUserIds.Contains(u.UserId))
                throw new InvalidOperationException(
                    $"Demo user id '{u.UserId}' collides with a baseline init.sql user id. FAIL.");

        if (BaselineUserIds.Contains(manifest.AdminUserId))
            throw new InvalidOperationException(
                $"Demo admin id '{manifest.AdminUserId}' collides with a baseline user id. FAIL.");
    }
}
