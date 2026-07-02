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
/// every user sits directly on their Organisation (S103 / ADR-038 retired the legacy
/// <c>enhed_label</c> model). The REPORTING tree keeps its realistic depth (span ~TargetSpan,
/// 12–18% managers, EXACTLY ONE root per Organisation, NO cycles — the manager of an employee
/// is always strictly closer to the root); reporting depth is a people-graph property,
/// independent of the now-flat ORG graph.
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
        : this(ScaleConfig.For(scale), seed, referenceDate)
    {
    }

    /// <summary>S114 — internal config-injection ctor (InternalsVisibleTo: the golden-legacy pin
    /// rebuilds today's no-override smoke config; the depth-assertion RED case injects a config
    /// that cannot reach manager depth 4).</summary>
    internal DemoGenerator(ScaleConfig config, int seed, DateOnly referenceDate)
    {
        _config = config;
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

        // 3b. S114 — the unit-derivation POST-PASS (Enhedsspor spine). A SEPARATE pass on a
        //     SECOND derived Random (seed ^ salt) — it NEVER touches _rng and never interleaves
        //     into the loops above, so every pre-existing draw is untouched (golden-pinned).
        //     Runs only for trees carrying a UnitSpanOverride; a no-override config emits NO
        //     unitPlans section at all.
        DeriveUnitPlans(users, manifest);

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
        //        MAO (depth 0). NO AFDELING/TEAM org rows — every user sits directly on the
        //        Organisation, which is BOTH the user-home org AND the reporting-tree root. ──
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

        // ── 2b. Users. EVERY user sits directly on the ORGANISATION (primary_org = the
        //        Organisation, tree_root = the Organisation). The top manager is the SINGLE
        //        reporting-tree root. ──
        int userSeq = 0;
        string NextUserId() => $"demo_{lower}_{(++userSeq):D4}";

        var treeUsers = new List<DemoUser>();

        // The top manager (reporting-tree root). primary_org = the Organisation.
        var topManager = MakeUser(NextUserId(), organisation.OrgId, root, profile, isSenior: true, leaverAllowed: false);
        topManager.IsManager = true;
        users.Add(topManager);
        treeUsers.Add(topManager);

        // Remaining users: all on the Organisation.
        var remaining = profile.TargetUsers - 1;
        for (var i = 0; i < remaining; i++)
        {
            var u = MakeUser(NextUserId(), organisation.OrgId, root, profile, isSenior: false, leaverAllowed: true);
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
        //    S114: an explicit ManagerCountOverride replaces the ratio (smoke needs ≥5 managers
        //    to fill 5 layers); absent, the legacy computation is byte-exact.
        var managerCount = profile.ManagerCountOverride
                           ?? Math.Max(2, (int)Math.Round(n * 0.14));
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

        if (profile.UnitSpanOverride is int forcedSpan)
        {
            // 2-alt. S114 DEPTH-FORCED layered spine: layers 1..3 sized by the override span
            //    (reserving ≥1 manager for every deeper layer), layer 4 = ALL the rest — so max
            //    manager depth == 4 with every depth 0–4 populated, for ANY M ≥ 5 (asserted;
            //    fail GENERATION loudly, never the load). Pure index math — NO RNG draw, so the
            //    _rng stream is identical to the legacy path's.
            var layers = ComputeForcedLayers(profile.OrganisationId, managers.Count, forcedSpan);

            // Parent assignment: layer d's managers round-robin over layer d-1 (even spread —
            //    every edge points strictly closer to the root ⇒ no cycle).
            var offset = 1; // managers[0] is the root (layer 0)
            var prevLayerStart = 0;
            var prevLayerCount = 1;
            for (var d = 1; d <= 4; d++)
            {
                for (var j = 0; j < layers[d]; j++)
                    AddEdge(managers[offset + j], managers[prevLayerStart + (j % prevLayerCount)]);
                prevLayerStart = offset;
                prevLayerCount = layers[d];
                offset += layers[d];
            }
        }
        else
        {
            // 2. LEGACY spine (byte-exact when no override): managers[0] is the root; lay the rest
            //    into BFS layers under it (≤ span children per manager). This is a pure index walk
            //    (deterministic). manager i (i≥1) reports to manager (i-1)/span — a strictly
            //    smaller index ⇒ always closer to the root ⇒ no cycle.
            for (var i = 1; i < managers.Count; i++)
            {
                var parentIndex = (i - 1) / span;
                AddEdge(managers[i], managers[parentIndex]);
            }
        }

        // 3. Leaves: round-robin across ALL managers (balanced load; the root also carries a few).
        for (var i = 0; i < leaves.Count; i++)
        {
            var mgr = managers[i % managers.Count];
            AddEdge(leaves[i], mgr);
        }
    }

    /// <summary>
    /// S114 — layer sizes for the depth-forced spine: index = depth 0..4. l0 = 1 (the root);
    /// l1..l3 = min(prevLayer × span, remaining − reserve) where reserve keeps ≥1 manager for
    /// every deeper layer; l4 = the remainder. Throws (generation-time, LOUD) when the tree
    /// cannot populate every depth 0–4 (M &lt; 5 or a squeezed middle layer).
    /// </summary>
    private static int[] ComputeForcedLayers(string organisationId, int managerCount, int forcedSpan)
    {
        if (forcedSpan < 1)
            throw new InvalidOperationException(
                $"[S114 unit-spine] org {organisationId}: UnitSpanOverride must be ≥ 1 (got {forcedSpan}).");
        if (managerCount < 5)
            throw new InvalidOperationException(
                $"[S114 unit-spine] org {organisationId}: {managerCount} managers cannot populate manager depths 0–4 " +
                "(need ≥ 5 — raise the tree's headcount or set ManagerCountOverride). Generation FAILED (never the load).");

        var layers = new int[5];
        layers[0] = 1;
        var remaining = managerCount - 1;
        for (var d = 1; d <= 3; d++)
        {
            var cap = layers[d - 1] * forcedSpan;
            var reserve = 4 - d; // ≥1 manager for each deeper layer (d+1 .. 4)
            layers[d] = Math.Min(cap, remaining - reserve);
            if (layers[d] < 1)
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {organisationId}: layer {d} would be empty at span {forcedSpan} " +
                    $"with {managerCount} managers. Generation FAILED (never the load).");
            remaining -= layers[d];
        }
        layers[4] = remaining;
        if (layers[4] < 1)
            throw new InvalidOperationException(
                $"[S114 unit-spine] org {organisationId}: no manager left for depth 4 at span {forcedSpan} " +
                $"with {managerCount} managers (max manager depth must be EXACTLY 4). Generation FAILED (never the load).");
        return layers;
    }

    private DemoUser MakeUser(string userId, string orgId, string treeRoot, TreeProfile profile, bool isSenior, bool leaverAllowed)
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

    // ═════════════════════════════════════════════════════════════════════════════════════════
    //  S114 / TASK-11400 — the unit-derivation post-pass (ADR-038 Enhedsspor spine).
    //
    //  Derives each override-tree's unit spine FROM its reporting-edge manager tree: manager m at
    //  depth d anchors unit U(m) of type [direktion,omrade,kontor,team,enhed][d]; unit parentage
    //  follows manager parentage; m is HOMED in U(m); U(m)'s members = m + m's NON-manager
    //  reports; m's manager-reports appear as CHILD UNITS, not member rows (single-unit
    //  membership + leader-is-member BY CONSTRUCTION). Deliberate, counted messiness: ~2
    //  leaderless units + ~3-5 sideways-homed NON-manager leaves per org, in DISJOINT units.
    //
    //  Determinism: consumes ONLY a derived Random(seed ^ salt) — NEVER _rng.
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Seed salt for the SECOND derived RNG stream (the unit post-pass).</summary>
    private const int UnitRngSalt = 0x0114_5EED;

    private static readonly string[] UnitTypeByDepth = { "direktion", "omrade", "kontor", "team", "enhed" };

    private void DeriveUnitPlans(List<DemoUser> users, DemoManifest manifest)
    {
        var overrideProfiles = _config.Trees.Where(t => t.UnitSpanOverride is not null).ToList();
        if (overrideProfiles.Count == 0)
            return; // legacy config: NO unitPlans section (byte-exact manifest — golden-pinned)

        var unitRng = new Random(unchecked(_seed ^ UnitRngSalt));
        var userById = users.ToDictionary(u => u.UserId, StringComparer.Ordinal);
        manifest.UnitPlans = new List<DemoUnitPlan>();

        foreach (var profile in overrideProfiles)
            manifest.UnitPlans.Add(DeriveOrgUnitPlan(profile, userById, manifest, unitRng));
    }

    private static DemoUnitPlan DeriveOrgUnitPlan(
        TreeProfile profile,
        IReadOnlyDictionary<string, DemoUser> userById,
        DemoManifest manifest,
        Random unitRng)
    {
        var org = profile.OrganisationId;
        var edges = manifest.ReportingEdges.Where(e => e.OrganisationId == org).ToList();
        var tree = manifest.Trees.Single(t => t.OrganisationId == org);

        // Manager set + parentage from the edges (first-appearance order is deterministic).
        var managerIds = new List<string>();
        var managerSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in edges)
            if (managerSet.Add(e.ManagerId))
                managerIds.Add(e.ManagerId);

        var managerOf = edges.ToDictionary(e => e.EmployeeId, e => e.ManagerId, StringComparer.Ordinal);

        // Depth of every manager (walk up the manager spine to the root).
        var depthOf = new Dictionary<string, int>(StringComparer.Ordinal);
        int DepthOf(string managerId)
        {
            if (depthOf.TryGetValue(managerId, out var d)) return d;
            d = managerOf.TryGetValue(managerId, out var parent) ? DepthOf(parent) + 1 : 0;
            depthOf[managerId] = d;
            return d;
        }
        foreach (var m in managerIds)
            _ = DepthOf(m);

        // ── The generation-time assertion: max manager depth == 4 AND ≥1 manager at EVERY
        //    depth 0–4. Fail GENERATION loudly, never the load. ──
        var managersPerDepth = new int[5];
        foreach (var m in managerIds)
        {
            var d = depthOf[m];
            if (d is < 0 or > 4)
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: manager {m} sits at depth {d} — the unit spine is CAPPED at " +
                    "depth 4 (an 'enhed' under an 'enhed' would 422 on PARTIAL-RANK at load). Generation FAILED.");
            managersPerDepth[d]++;
        }
        for (var d = 0; d <= 4; d++)
            if (managersPerDepth[d] == 0)
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: NO manager at depth {d} — every depth 0–4 must be populated " +
                    "(all 5 unit types per org). Generation FAILED (never the load).");

        // ── Units: one per manager, parent-first (depth, then user id — both deterministic). ──
        var orderedManagers = managerIds
            .OrderBy(m => depthOf[m])
            .ThenBy(m => m, StringComparer.Ordinal)
            .ToList();

        var unitByKey = new Dictionary<string, DemoUnit>(StringComparer.Ordinal);
        var usedNamesByParent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var units = new List<DemoUnit>();

        foreach (var m in orderedManagers)
        {
            var depth = depthOf[m];
            var parentKey = managerOf.TryGetValue(m, out var pm) ? pm : null;
            var name = DrawUnitName(unitRng, depth, parentKey ?? "", usedNamesByParent);

            var unit = new DemoUnit
            {
                UnitKey = m,
                ParentUnitKey = parentKey,
                Type = UnitTypeByDepth[depth],
                Name = name,
                Depth = depth,
                LeaderUserId = m, // the anchor leads U(m) — leaderless messiness clears this below
                MemberUserIds = new List<string> { m },
            };
            unitByKey[m] = unit;
            units.Add(unit);
        }

        // Members: every NON-manager report joins its manager's unit (managers are already the
        // sole member of their OWN unit — their manager-reports are child units, not members).
        foreach (var e in edges)
            if (!managerSet.Contains(e.EmployeeId))
                unitByKey[e.ManagerId].MemberUserIds.Add(e.EmployeeId);

        // ── Messiness: small, deliberate, counted, DISJOINT units (each messy unit hosts exactly
        //    ONE kind). Sizes adapt to small trees (smoke) but the LEDGER records actuals and the
        //    verifier asserts the ledger EXACTLY. ──
        var messyUnits = new HashSet<string>(StringComparer.Ordinal);

        // (a) ~2 leaderless units: skip the leader appointment (the anchor STAYS a member — the
        //     designed "Ingen leder i enheden" banner; its refererer-opad list may include the
        //     anchor, the accepted cosmetic quirk). The depth-0 direktion is excluded (keep the
        //     org head intact) and ≥2 pairable units are left for the sideways cases.
        var leaderlessEligible = units.Where(u => u.Depth >= 1).Select(u => u.UnitKey).ToList();
        var leaderlessTarget = Math.Min(2, Math.Max(0, leaderlessEligible.Count - 2));
        var leaderlessKeys = new List<string>();
        for (var i = 0; i < leaderlessTarget; i++)
        {
            var pick = leaderlessEligible[unitRng.Next(leaderlessEligible.Count)];
            if (!messyUnits.Add(pick)) { i--; continue; } // re-draw an already-picked unit
            unitByKey[pick].LeaderUserId = null;
            leaderlessKeys.Add(pick);
        }

        // (b) ~3-5 sideways-homed members — NON-manager leaves ONLY (re-homing a MANAGER silently
        //     strips their leaderships [D3] and would decapitate their unit, corrupting the
        //     ledger — the hard rule). Source keeps ≥1 leaf; target has a leader; source, target
        //     and leaderless units are pairwise DISJOINT.
        var sidewaysCases = new List<DemoSidewaysCase>();
        var sidewaysTarget = 3 + unitRng.Next(3); // 3..5
        for (var i = 0; i < sidewaysTarget; i++)
        {
            var sources = units
                .Where(u => !messyUnits.Contains(u.UnitKey) && u.Depth >= 1 && u.MemberUserIds.Count >= 3)
                .ToList();
            if (sources.Count == 0) break;
            var source = sources[unitRng.Next(sources.Count)];

            // Prefer a same-parent sibling target; fall back to any same-depth unit. Both keep
            // the amber "Ret" flow honest (a real in-unit leader target exists).
            var targets = units
                .Where(u => !messyUnits.Contains(u.UnitKey) && u.UnitKey != source.UnitKey
                            && u.LeaderUserId is not null
                            && string.Equals(u.ParentUnitKey, source.ParentUnitKey, StringComparison.Ordinal))
                .ToList();
            if (targets.Count == 0)
                targets = units
                    .Where(u => !messyUnits.Contains(u.UnitKey) && u.UnitKey != source.UnitKey
                                && u.LeaderUserId is not null && u.Depth == source.Depth)
                    .ToList();
            if (targets.Count == 0) break;
            var target = targets[unitRng.Next(targets.Count)];

            // A NON-manager leaf of the source (index 0 is the anchor manager — never eligible).
            var leaves = source.MemberUserIds.Where(id => !managerSet.Contains(id)).ToList();
            var mover = leaves[unitRng.Next(leaves.Count)];

            source.MemberUserIds.Remove(mover);
            target.MemberUserIds.Add(mover);
            messyUnits.Add(source.UnitKey);
            messyUnits.Add(target.UnitKey);
            sidewaysCases.Add(new DemoSidewaysCase { UserId = mover, FromUnitKey = source.UnitKey, ToUnitKey = target.UnitKey });
        }

        // ── Post-derivation invariants (fail generation loudly). ──
        AssertUnitPlanInvariants(org, tree.RootEmployeeId, units, leaderlessKeys, sidewaysCases,
            managerSet, userById);

        return new DemoUnitPlan
        {
            OrganisationId = org,
            ManagersPerDepth = managersPerDepth.ToList(),
            Units = units,
            LeaderlessUnitKeys = leaderlessKeys,
            SidewaysCases = sidewaysCases,
        };
    }

    /// <summary>Deterministic Danish unit name, unique per parent (case-insensitive — the DB
    /// active-name index is lower(name)-scoped). ONE rng draw per unit regardless of collisions
    /// (collisions resolve by pool probing, then a numbered suffix).</summary>
    private static string DrawUnitName(
        Random unitRng, int depth, string parentKey, Dictionary<string, HashSet<string>> usedNamesByParent)
    {
        if (!usedNamesByParent.TryGetValue(parentKey, out var used))
            usedNamesByParent[parentKey] = used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (depth == 0)
        {
            var direktion = "Direktionen";
            used.Add(direktion);
            return direktion;
        }

        var pool = depth switch
        {
            1 => DanishPools.OmraadeNames,
            2 => DanishPools.KontorNames,
            3 => DanishPools.TeamNames,
            _ => DanishPools.EnhedNames,
        };

        var start = unitRng.Next(pool.Length);
        for (var k = 0; k < pool.Length; k++)
        {
            var candidate = pool[(start + k) % pool.Length];
            if (used.Add(candidate))
                return candidate;
        }
        for (var n = 2; ; n++)
        {
            var candidate = $"{pool[start]} {n}";
            if (used.Add(candidate))
                return candidate;
        }
    }

    /// <summary>S114 — the post-derivation generation-time invariants: single-unit-membership
    /// PARTITION over the org's active users, leader-is-member, PARTIAL-RANK validity (child =
    /// parent depth + 1), per-parent name uniqueness (tracked-set parity), non-manager sideways
    /// movers, and messy-unit disjointness.</summary>
    private static void AssertUnitPlanInvariants(
        string org,
        string rootEmployeeId,
        List<DemoUnit> units,
        List<string> leaderlessKeys,
        List<DemoSidewaysCase> sidewaysCases,
        HashSet<string> managerSet,
        IReadOnlyDictionary<string, DemoUser> userById)
    {
        var unitByKey = units.ToDictionary(u => u.UnitKey, StringComparer.Ordinal);

        // Partition: every ACTIVE user of the org in EXACTLY one member list.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in units)
            foreach (var member in u.MemberUserIds)
                if (!seen.Add(member))
                    throw new InvalidOperationException(
                        $"[S114 unit-spine] org {org}: user {member} is a member of MORE THAN ONE unit (single-unit membership violated).");
        var activeOrgUsers = userById.Values.Where(u => u.OrganisationId == org && u.IsActive).Select(u => u.UserId).ToList();
        foreach (var id in activeOrgUsers)
            if (!seen.Contains(id))
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: active user {id} is NOT homed in any unit (homing totality violated).");
        if (seen.Count != activeOrgUsers.Count)
            throw new InvalidOperationException(
                $"[S114 unit-spine] org {org}: unit member lists carry {seen.Count} users but the org has {activeOrgUsers.Count} active users.");

        foreach (var u in units)
        {
            // Leader-is-member (when a leader exists) + the anchor is always homed in its own unit.
            if (u.LeaderUserId is not null && !u.MemberUserIds.Contains(u.LeaderUserId))
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: unit {u.Name} ({u.UnitKey}) leader {u.LeaderUserId} is not a member.");
            if (!u.MemberUserIds.Contains(u.UnitKey))
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: unit {u.Name} anchor {u.UnitKey} is not homed in its own unit.");

            // PARTIAL-RANK: the root unit is the direktion; every child is exactly one rank deeper.
            if (u.ParentUnitKey is null)
            {
                if (u.Depth != 0 || u.UnitKey != rootEmployeeId)
                    throw new InvalidOperationException(
                        $"[S114 unit-spine] org {org}: top-level unit {u.Name} is not the root direktion.");
            }
            else if (u.Depth != unitByKey[u.ParentUnitKey].Depth + 1)
            {
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: unit {u.Name} depth {u.Depth} is not parent depth + 1.");
            }
        }

        // Per-parent name uniqueness (paranoia parity with the tracked sets).
        foreach (var group in units.GroupBy(u => u.ParentUnitKey ?? ""))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in group)
                if (!names.Add(u.Name))
                    throw new InvalidOperationException(
                        $"[S114 unit-spine] org {org}: duplicate sibling unit name '{u.Name}' (the API 409s it at load).");
        }

        // Messiness ledger sanity: sideways movers are NON-managers; messy units pairwise disjoint.
        var messy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in leaderlessKeys)
            if (!messy.Add(key))
                throw new InvalidOperationException($"[S114 unit-spine] org {org}: leaderless unit {key} listed twice.");
        foreach (var c in sidewaysCases)
        {
            if (managerSet.Contains(c.UserId))
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: sideways case {c.UserId} is a MANAGER (D3 decapitation hazard — hard rule).");
            if (!messy.Add(c.FromUnitKey) || !messy.Add(c.ToUnitKey))
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: sideways case {c.UserId} reuses a messy unit (disjointness violated).");
            if (unitByKey[c.ToUnitKey].LeaderUserId is null)
                throw new InvalidOperationException(
                    $"[S114 unit-spine] org {org}: sideways target {c.ToUnitKey} has no leader (the 'Ret' flow needs one).");
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
