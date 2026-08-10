using System.Net;
using System.Text.Json;
using StatsTid.Tools.DemoSeed.Model;

namespace StatsTid.Tools.DemoSeed.Loading;

/// <summary>
/// S84 / TASK-8403 — the post-boot API loader. Reads the manifest, authenticates as the demo
/// GLOBAL_ADMIN, and drives the live API (event-emitting paths, OQ-4) to build the reporting
/// trees, grant privileged roles, set part-time profiles, create the activity slice + vikars,
/// and apply the messy-case steps. IDEMPOTENT: every write is skip-if-present (the import is a
/// TRUE no-op on re-run; profile PUT skips when already at the target fraction; vikar create
/// probes the GET first; the period send is conflict-tolerant).
///
/// <para>S127 / TASK-12701a — the activity stage now writes a COMPLETE month: absences, self-recorded
/// work time, and the project allocations that reconcile against it day by day. Before this it wrote
/// absences ONLY, which is why exactly one of the demo world's 375 approval periods passed the
/// workday-coverage check.</para>
///
/// <para>S127 / TASK-12701b — the period call is <c>POST /api/approval/send</c> (the retired
/// <c>/submit</c> is gone), and a period that does NOT reach its manifest outcome is counted in
/// <see cref="LoadResult.PeriodOutcomeFailures"/> so it can fail the process. A warning alone never
/// could: warnings do not affect the exit code, so before this the loader returned 0 having failed
/// all 374 sends.</para>
/// </summary>
public sealed class DemoLoader
{
    private readonly ApiClient _api;
    private readonly DemoManifest _manifest;
    private readonly int _batchSize;
    private readonly Action<string> _log;

    public DemoLoader(ApiClient api, DemoManifest manifest, int batchSize, Action<string> log)
    {
        _api = api;
        _manifest = manifest;
        _batchSize = batchSize;
        _log = log;
    }

    public sealed class LoadResult
    {
        public int EdgesImported { get; set; }
        public int EdgesSkipped { get; set; }
        public int RolesGranted { get; set; }
        public int RolesSkipped { get; set; }
        public int ProfilesSet { get; set; }
        public int ProfilesSkipped { get; set; }
        public int AbsencesSaved { get; set; }

        /// <summary>S127 — project-allocation rows written (Skema <c>entries</c>).</summary>
        public int AllocationsSaved { get; set; }

        /// <summary>S127 — self-recorded work days written (Skema <c>workTime</c>).</summary>
        public int WorkDaysSaved { get; set; }

        /// <summary>S127 — activity months skipped whole because all three collections were already
        /// present (the re-run path).</summary>
        public int MonthsAlreadyComplete { get; set; }

        /// <summary>S127 / TASK-12701b — periods SENT this run (POST /api/approval/send → 200).</summary>
        public int PeriodsSent { get; set; }

        /// <summary>S127 / TASK-12701b — sends that 409'd because the row was already past a sendable
        /// source state (the re-run path). A skip, NOT a success: on a first load a period that never
        /// landed has no row and therefore cannot 409.</summary>
        public int PeriodsAlreadySent { get; set; }

        public int PeriodsApproved { get; set; }
        public int PeriodsRejected { get; set; }

        /// <summary>
        /// S127 / TASK-12701b (AC-14b) — activity periods that did NOT reach their manifest outcome:
        /// a non-200/409 send, or a failed approve/reject after a successful send.
        ///
        /// <para>This exists because a WARNING is invisible to a caller: warnings never affected the
        /// exit code, so the loader used to return 0 having failed every single send. Program.cs
        /// turns a non-zero count here into a non-zero exit.</para>
        /// </summary>
        public int PeriodOutcomeFailures { get; set; }
        public int VikarsCreated { get; set; }
        public int VikarsSkipped { get; set; }
        public int MessyApplied { get; set; }

        // S114 — the unit-spine stages (units → homing → leaders, canonical order).
        public int UnitsCreated { get; set; }
        public int UnitsSkipped { get; set; }
        public int MembersHomed { get; set; }
        public int MembersHomedSkipped { get; set; }
        public int LeadersAppointed { get; set; }

        /// <summary>S114 — every 4xx the unit stages saw (ZERO expected on a clean load AND on a
        /// re-run — the probe-first design makes any 4xx a real finding).</summary>
        public int UnitStageClientErrors { get; set; }

        public List<string> Warnings { get; } = new();
    }

    public async Task<LoadResult> LoadAsync(CancellationToken ct)
    {
        var result = new LoadResult();

        _log($"Authenticating as {_manifest.AdminUserId} ...");
        await _api.LoginAsync(_manifest.AdminUserId, _manifest.Password, ct);
        _log("Authenticated.");

        await ImportTreesAsync(result, ct);
        await GrantRolesAsync(result, ct);

        // S114 unit-spine stages — CANONICAL order (UnitLoadPlanner.CanonicalStageOrder):
        // (a) units parent-first → (b) home ALL members probe-first → (c) leaders LAST.
        // Leaders must come last twice over: POST …/leaders 422s a non-member, and a later
        // re-home would SILENTLY strip the designation again (D3).
        await CreateUnitsAsync(result, ct);
        await HomeMembersAsync(result, ct);
        await AppointLeadersAsync(result, ct);

        await SetProfilesAsync(result, ct);
        await CreateActivityAsync(result, ct);
        await CreateVikarsAsync(result, ct);
        await ApplyMessyCasesAsync(result, ct);

        return result;
    }

    // ── Reporting trees: batched import per tree ──
    private async Task ImportTreesAsync(LoadResult result, CancellationToken ct)
    {
        foreach (var tree in _manifest.Trees)
        {
            var edges = _manifest.ReportingEdges
                .Where(e => e.OrganisationId == tree.OrganisationId)
                .ToList();
            _log($"Importing {edges.Count} edges for tree {tree.OrganisationId} in batches of {_batchSize} ...");

            for (var offset = 0; offset < edges.Count; offset += _batchSize)
            {
                var batch = edges.Skip(offset).Take(_batchSize).ToList();
                var payload = new
                {
                    organisationId = tree.OrganisationId,
                    rows = batch.Select(e => new
                    {
                        employeeId = e.EmployeeId,
                        managerId = e.ManagerId,
                        effectiveFrom = e.EffectiveFrom,
                    }).ToList(),
                };
                var (status, body) = await _api.ImportReportingLinesAsync(payload, ct);
                if (status != HttpStatusCode.OK)
                    throw new InvalidOperationException(
                        $"Import batch failed for tree {tree.OrganisationId} (offset {offset}): {(int)status} {body}");

                var (imported, skipped) = ParseImportCounts(body);
                result.EdgesImported += imported;
                result.EdgesSkipped += skipped;
            }
            _log($"  tree {tree.OrganisationId}: imported so far={result.EdgesImported}, skipped={result.EdgesSkipped}");
        }
    }

    private static (int Imported, int Skipped) ParseImportCounts(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            int imported = root.TryGetProperty("imported", out var i) && i.TryGetInt32(out var iv) ? iv : 0;
            int superseded = root.TryGetProperty("superseded", out var su) && su.TryGetInt32(out var suv) ? suv : 0;
            int skipped = root.TryGetProperty("skipped", out var s) && s.TryGetInt32(out var sv) ? sv : 0;
            return (imported + superseded, skipped);
        }
        catch (JsonException) { return (0, 0); }
    }

    // ── Privileged role grants (LOCAL_HR / LOCAL_LEADER). Idempotent: the role_assignments
    //    UNIQUE(user_id, role_id, org_id) makes a duplicate grant fail; we treat a non-OK as a
    //    likely-already-present skip (logged) rather than fatal. ──
    private async Task GrantRolesAsync(LoadResult result, CancellationToken ct)
    {
        if (_manifest.RoleGrants.Count == 0)
        {
            _log("Privileged roles: none to grant via API (SQL-seeded in 99-demo-seed.sql — the grant endpoint has a product bug; see SPRINT-84).");
            return;
        }
        _log($"Granting {_manifest.RoleGrants.Count} privileged roles ...");
        foreach (var g in _manifest.RoleGrants)
        {
            var (status, body) = await _api.GrantRoleAsync(new
            {
                userId = g.UserId,
                roleId = g.RoleId,
                orgId = g.OrgId,
                scopeType = g.ScopeType,
            }, ct);
            if (status == HttpStatusCode.OK || status == HttpStatusCode.Created)
                result.RolesGranted++;
            else if (status == HttpStatusCode.Conflict)
                // A duplicate grant trips the role_assignments UNIQUE(user_id, role_id, org_id)
                // constraint → treat as already-present (idempotent re-run).
                result.RolesSkipped++;
            else
                // A 500 here is the known product bug in POST /api/admin/roles/grant
                // (its role_assignment_audit INSERT targets columns the schema does not have:
                // performed_by/performed_at + a TEXT details + action 'GRANT' vs 'GRANTED').
                // Surface it as a WARNING rather than masking it as a skip.
                result.Warnings.Add($"grant role {g.RoleId} for {g.UserId} → {(int)status} {Trunc(body)}");
        }
        _log($"  roles granted={result.RolesGranted}, skipped(existing)={result.RolesSkipped}");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  S114 / TASK-11400 — the unit-spine stages. All three are PROBE-FIRST idempotent: a
    //  re-run against an already-loaded DB makes zero mutating calls (and zero 4xx).
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>unitKey (anchor manager id) → SERVER unit GUID, filled by stage (a) and consumed
    /// by stages (b)+(c).</summary>
    private readonly Dictionary<string, Guid> _unitIdByKey = new(StringComparer.Ordinal);

    // ── Stage (a): create units PARENT-FIRST via forest-probe-then-create. One forest GET per
    //    org; match existing by org + parent-chain + name; create only the missing; capture the
    //    server GUIDs; NEVER delete (an owner-renamed unit simply doesn't match and is left alone). ──
    private async Task CreateUnitsAsync(LoadResult result, CancellationToken ct)
    {
        if (_manifest.UnitPlans is not { Count: > 0 })
        {
            _log("Units: no unit plans in this manifest (pre-S114 or no-override) — stages skipped.");
            return;
        }

        _log($"[{UnitLoadPlanner.CanonicalStageOrder[0]}] creating unit spines for {_manifest.UnitPlans.Count} orgs ...");
        foreach (var plan in _manifest.UnitPlans)
        {
            var (fStatus, fBody) = await _api.GetUnitsForestAsync(ct);
            if (fStatus != HttpStatusCode.OK)
                throw new InvalidOperationException($"Forest probe failed for {plan.OrganisationId}: {(int)fStatus} {Trunc(fBody)}");

            var existing = ParseExistingUnits(fBody, plan.OrganisationId);
            var actions = UnitLoadPlanner.PlanUnitCreates(plan, existing);

            var done = 0;
            foreach (var action in actions)
            {
                if (action.AlreadyExists)
                {
                    _unitIdByKey[action.Unit.UnitKey] = action.ExistingUnitId!.Value;
                    result.UnitsSkipped++;
                }
                else
                {
                    Guid? parentId = null;
                    if (action.Unit.ParentUnitKey is string pk)
                    {
                        if (!_unitIdByKey.TryGetValue(pk, out var resolved))
                        {
                            result.Warnings.Add($"unit {plan.OrganisationId}/{action.Unit.Name}: parent {pk} unresolved — skipped");
                            continue;
                        }
                        parentId = resolved;
                    }

                    var (status, body) = await _api.CreateUnitAsync(new
                    {
                        organisationId = plan.OrganisationId,
                        parentUnitId = parentId,
                        type = action.Unit.Type,
                        name = action.Unit.Name,
                    }, ct);

                    if (status == HttpStatusCode.Created && TryParseUnitId(body, out var unitId))
                    {
                        _unitIdByKey[action.Unit.UnitKey] = unitId;
                        result.UnitsCreated++;
                    }
                    else
                    {
                        if ((int)status is >= 400 and < 500) result.UnitStageClientErrors++;
                        result.Warnings.Add($"unit create {plan.OrganisationId}/{action.Unit.Name} → {(int)status} {Trunc(body)}");
                    }
                }

                if (++done % _batchSize == 0)
                    _log($"  {plan.OrganisationId}: {done}/{actions.Count} units processed ...");
            }
            _log($"  {plan.OrganisationId}: units created={result.UnitsCreated} matched-existing={result.UnitsSkipped} (running totals)");
        }
    }

    // ── Stage (b): home ALL members PROBE-FIRST. One roster GET per org supplies every person's
    //    CURRENT unitId (skip when already correct — a re-run makes zero writes); each actual
    //    homing PUT carries the FRESHLY-FETCHED user ETag (never a blanket If-Match "1"). ──
    private async Task HomeMembersAsync(LoadResult result, CancellationToken ct)
    {
        if (_manifest.UnitPlans is not { Count: > 0 })
            return;

        _log($"[{UnitLoadPlanner.CanonicalStageOrder[1]}] homing members ...");
        foreach (var plan in _manifest.UnitPlans)
        {
            var (rStatus, rBody) = await _api.GetRosterAsync(plan.OrganisationId, ct);
            if (rStatus != HttpStatusCode.OK)
                throw new InvalidOperationException($"Roster probe failed for {plan.OrganisationId}: {(int)rStatus} {Trunc(rBody)}");

            var currentUnitByUser = ParseRosterUnits(rBody);
            var actions = UnitLoadPlanner.PlanHomingActions(plan, _unitIdByKey, currentUnitByUser);
            var skipped = plan.Units.Sum(u => u.MemberUserIds.Count) - actions.Count;
            result.MembersHomedSkipped += skipped;

            var done = 0;
            foreach (var (userId, unitKey) in actions)
            {
                var (gStatus, version, gBody) = await _api.GetUserAsync(userId, ct);
                if (gStatus != HttpStatusCode.OK || version is null)
                {
                    if ((int)gStatus is >= 400 and < 500) result.UnitStageClientErrors++;
                    result.Warnings.Add($"homing GET {userId} → {(int)gStatus} {Trunc(gBody)}");
                    continue;
                }

                var (pStatus, pBody) = await _api.PutUserUnitAsync(userId,
                    new { unitId = _unitIdByKey[unitKey] }, version.Value, ct);
                if (pStatus == HttpStatusCode.OK)
                {
                    result.MembersHomed++;
                }
                else
                {
                    if ((int)pStatus is >= 400 and < 500) result.UnitStageClientErrors++;
                    result.Warnings.Add($"homing PUT {userId} → {unitKey} → {(int)pStatus} {Trunc(pBody)}");
                }

                if (++done % _batchSize == 0)
                    _log($"  {plan.OrganisationId}: {done}/{actions.Count} homings applied ...");
            }
            _log($"  {plan.OrganisationId}: homed={result.MembersHomed} skipped(already-correct)={result.MembersHomedSkipped} (running totals)");
        }
    }

    // ── Stage (c): appoint leaders LAST (the API 422s a non-member; homing preceded, so every
    //    designee is a member; POST is idempotent — 200 whether fresh or already designated). ──
    private async Task AppointLeadersAsync(LoadResult result, CancellationToken ct)
    {
        if (_manifest.UnitPlans is not { Count: > 0 })
            return;

        _log($"[{UnitLoadPlanner.CanonicalStageOrder[2]}] appointing leaders ...");
        foreach (var plan in _manifest.UnitPlans)
        {
            foreach (var (unitKey, leaderUserId) in UnitLoadPlanner.PlanLeaderAppointments(plan))
            {
                if (!_unitIdByKey.TryGetValue(unitKey, out var unitId))
                {
                    result.Warnings.Add($"leader {leaderUserId}: unit {unitKey} unresolved — skipped");
                    continue;
                }
                var (status, body) = await _api.DesignateUnitLeaderAsync(unitId, new { userId = leaderUserId }, ct);
                if (status == HttpStatusCode.OK)
                {
                    result.LeadersAppointed++;
                }
                else
                {
                    if ((int)status is >= 400 and < 500) result.UnitStageClientErrors++;
                    result.Warnings.Add($"leader {leaderUserId} on {unitKey} → {(int)status} {Trunc(body)}");
                }
            }
            _log($"  {plan.OrganisationId}: leaders appointed={result.LeadersAppointed} (deliberately leaderless={plan.LeaderlessUnitKeys.Count}; running total)");
        }
    }

    /// <summary>Flattens ONE Organisation's unit sub-forest out of the GET /api/admin/units/forest
    /// envelope ({ forest: [maoNode { organisations: [orgNode { units: [nested…] }] }] }).</summary>
    private static List<UnitLoadPlanner.ExistingUnit> ParseExistingUnits(string forestBody, string organisationId)
    {
        var result = new List<UnitLoadPlanner.ExistingUnit>();
        using var doc = JsonDocument.Parse(forestBody);
        if (!doc.RootElement.TryGetProperty("forest", out var forest) || forest.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var mao in forest.EnumerateArray())
        {
            if (!mao.TryGetProperty("organisations", out var orgs) || orgs.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var org in orgs.EnumerateArray())
            {
                if (!org.TryGetProperty("orgId", out var orgId) || orgId.GetString() != organisationId)
                    continue;
                if (org.TryGetProperty("units", out var units) && units.ValueKind == JsonValueKind.Array)
                    foreach (var u in units.EnumerateArray())
                        FlattenUnitNode(u, result);
            }
        }
        return result;
    }

    private static void FlattenUnitNode(JsonElement node, List<UnitLoadPlanner.ExistingUnit> into)
    {
        var unitId = node.GetProperty("unitId").GetGuid();
        Guid? parentId = node.TryGetProperty("parentUnitId", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetGuid()
            : null;
        var type = node.GetProperty("type").GetString() ?? "";
        var name = node.GetProperty("name").GetString() ?? "";
        into.Add(new UnitLoadPlanner.ExistingUnit(unitId, parentId, type, name));

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                FlattenUnitNode(child, into);
    }

    /// <summary>employeeId → current unitId (null = homed at the Organisation) from the unit-tagged
    /// roster read GET /api/admin/reporting-lines/tree/{org}/medarbejdere.</summary>
    private static Dictionary<string, Guid?> ParseRosterUnits(string rosterBody)
    {
        var map = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(rosterBody);
        if (!doc.RootElement.TryGetProperty("employees", out var employees) || employees.ValueKind != JsonValueKind.Array)
            return map;
        foreach (var e in employees.EnumerateArray())
        {
            var id = e.GetProperty("employeeId").GetString();
            if (id is null) continue;
            Guid? unitId = e.TryGetProperty("unitId", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetGuid()
                : null;
            map[id] = unitId;
        }
        return map;
    }

    private static bool TryParseUnitId(string body, out Guid unitId)
    {
        unitId = Guid.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("unitId", out var u) && u.TryGetGuid(out unitId);
        }
        catch (JsonException) { return false; }
    }

    // ── Part-time / position via the profile PUT (GET version → If-Match PUT, EffectiveFrom=today) ──
    private async Task SetProfilesAsync(LoadResult result, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        _log($"Setting {_manifest.ProfileEdits.Count} part-time/position profiles ...");
        foreach (var p in _manifest.ProfileEdits)
        {
            var (getStatus, version, getBody) = await _api.GetEmployeeProfileAsync(p.EmployeeId, ct);
            if (getStatus != HttpStatusCode.OK || version is null)
            {
                result.Warnings.Add($"profile GET {p.EmployeeId} → {(int)getStatus} (seeder may not have created the row yet)");
                continue;
            }

            // Idempotency: skip if already at the target fraction.
            if (ProfileAlreadyMatches(getBody, p.PartTimeFraction, p.Position))
            {
                result.ProfilesSkipped++;
                continue;
            }

            var (putStatus, putBody) = await _api.PutEmployeeProfileAsync(p.EmployeeId, new
            {
                effectiveFrom = today,
                partTimeFraction = p.PartTimeFraction,
                position = p.Position,
                // S103 / TASK-10305 (Enhedsspor Phase 1a): the enhed_label display column was dropped
                // with the legacy Enhed model, so the profile PUT no longer carries it (unit-based
                // display returns in S104+).
            }, version.Value, ct);

            if (putStatus == HttpStatusCode.OK)
                result.ProfilesSet++;
            else
                result.Warnings.Add($"profile PUT {p.EmployeeId} → {(int)putStatus} {Trunc(putBody)}");
        }
        _log($"  profiles set={result.ProfilesSet}, skipped(matching)={result.ProfilesSkipped}");
    }

    private static bool ProfileAlreadyMatches(string body, decimal fraction, string position)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("partTimeFraction", out var f)) return false;
            var current = f.GetDecimal();
            return Math.Abs(current - fraction) < 0.0005m;
        }
        catch (JsonException) { return false; }
    }

    // ── Activity: the complete skema month (absences + allocations + work time) + a period
    //    transition. S127 / TASK-12701a added the allocation + work-time collections;
    //    TASK-12701b converted the period call to POST /api/approval/send. ──
    private async Task CreateActivityAsync(LoadResult result, CancellationToken ct)
    {
        _log($"Creating activity for {_manifest.Activity.Count} employees ...");
        foreach (var a in _manifest.Activity)
        {
            // 1. Save the month — absences, project allocations and self-recorded work time — but
            //    IDEMPOTENTLY, and PER COLLECTION.
            //
            //    The skema save is event-sourced: absences and time entries APPEND a fresh event
            //    per call (their projections key on event_id, not on (employee, date)), so a blind
            //    re-save accumulates duplicates. For entries that is not merely untidy — doubling
            //    the allocations while work time stays put (it is a latest-wins upsert under the
            //    outbox_id guard) leaves every day UNBALANCED, which is exactly what the S127
            //    submit-time gate rejects. So the month is probed first and each collection is sent
            //    only if it is short. The save is one atomic transaction, so a collection is either
            //    fully present or fully absent — the count comparison is exact, never partial.
            // Null ⇒ an unfilled month (a pre-S127 manifest, which must still load).
            var allocations = a.Allocations ?? new List<DemoAllocation>();
            var workDays = a.WorkTime ?? new List<DemoWorkDay>();
            var wantAbsences = a.Absences.Count;
            var wantAllocations = allocations.Count;
            var wantWorkDays = workDays.Count;

            if (wantAbsences + wantAllocations + wantWorkDays > 0)
            {
                var (probeStatus, haveAbsences, haveEntries, haveWorkDays) =
                    await _api.GetSkemaMonthCountsAsync(a.EmployeeId, a.Year, a.Month, ct);
                var probed = probeStatus == HttpStatusCode.OK;

                var sendAbsences = !(probed && haveAbsences >= wantAbsences);
                var sendAllocations = !(probed && haveEntries >= wantAllocations);
                var sendWorkDays = !(probed && haveWorkDays >= wantWorkDays);

                if (!sendAbsences && !sendAllocations && !sendWorkDays)
                {
                    result.MonthsAlreadyComplete++;
                }
                else
                {
                    var (skStatus, skBody) = await _api.SkemaSaveAsync(a.EmployeeId, new
                    {
                        year = a.Year,
                        month = a.Month,
                        absences = sendAbsences
                            ? a.Absences.Select(ab => new
                            {
                                date = ab.Date,
                                absenceType = ab.AbsenceType,
                                hours = ab.Hours,
                            }).ToList<object>()
                            : new List<object>(),
                        // The allocation side of the gate: NORMAL entries with a non-null TaskId.
                        // The server takes TaskId from projectCode (SkemaEndpoints.cs:1609), so the
                        // code must name a project in the employee's OWN org — the generator picks
                        // from that org's catalogue.
                        entries = sendAllocations
                            ? allocations.Select(al => new
                            {
                                date = al.Date,
                                projectCode = al.ProjectCode,
                                hours = al.Hours,
                            }).ToList<object>()
                            : new List<object>(),
                        // The worked side of the gate: interval hours + manual hours. Intervals
                        // only, so the persisted total is exactly the interval duration.
                        workTime = sendWorkDays
                            ? workDays.Select(w => new
                            {
                                date = w.Date,
                                intervals = new[] { new { start = w.Start, end = w.End } },
                                manualHours = 0m,
                            }).ToList<object>()
                            : new List<object>(),
                    }, ct);

                    if (skStatus == HttpStatusCode.OK)
                    {
                        if (sendAbsences) result.AbsencesSaved += wantAbsences;
                        if (sendAllocations) result.AllocationsSaved += wantAllocations;
                        if (sendWorkDays) result.WorkDaysSaved += wantWorkDays;
                    }
                    else if (skStatus == HttpStatusCode.Conflict)
                        { /* period already locked (APPROVED) on a prior run — idempotent skip */ }
                    else
                        result.Warnings.Add($"skema save {a.EmployeeId} {a.Year}-{a.Month} → {(int)skStatus} {Trunc(skBody)}");
                }
            }

            // 2. The SEND act (S127 / TASK-12701b — POST /api/approval/send).
            //
            //    Month-keyed: {employeeId, year, month}. The server derives [monthStart, monthEnd]
            //    and resolves org / agreement / okVersion itself, so the manifest's stored OrgId /
            //    AgreementCode / OkVersion are NOT sent — they are generation-time values and a
            //    caller-supplied dimension is the P4 hole the send command closed.
            if (a.PeriodOutcome == "NONE")
                continue;

            var (sendStatus, periodId, sendBody) = await _api.SendPeriodAsync(a.EmployeeId, a.Year, a.Month, ct);

            if (sendStatus == HttpStatusCode.Conflict)
            {
                // The row is already past a sendable source state (EMPLOYEE_APPROVED or APPROVED) —
                // the genuine re-run path, and the ONLY status treated as a skip.
                //
                // ⚠ A 409 is a skip and NOT evidence of success. On a FIRST load a period that never
                // landed produces no row at all, so it cannot 409 — it re-attempts and fails again.
                // The proof that the world is right is the verifier's manifest-derived status-count
                // check (DemoVerifier check 21), never this branch.
                result.PeriodsAlreadySent++;
                continue;
            }
            if (sendStatus != HttpStatusCode.OK || periodId is null)
            {
                // 422 = coverage or allocation refused the month; 403 = the floor; anything else is
                // worse. None of them is a skip: the manifest says this month must reach a
                // manager-visible state and it did not.
                result.PeriodOutcomeFailures++;
                result.Warnings.Add($"send {a.EmployeeId} {a.Year}-{a.Month:00} → {(int)sendStatus} {Trunc(sendBody)}");
                continue;
            }
            result.PeriodsSent++;

            switch (a.PeriodOutcome)
            {
                case "APPROVED":
                    var (apStatus, apBody) = await _api.ApprovePeriodAsync(periodId.Value, ct);
                    if (apStatus == HttpStatusCode.OK) result.PeriodsApproved++;
                    else
                    {
                        result.PeriodOutcomeFailures++;
                        result.Warnings.Add($"approve {a.EmployeeId} → {(int)apStatus} {Trunc(apBody)}");
                    }
                    break;
                case "REJECTED":
                    var (rjStatus, rjBody) = await _api.RejectPeriodAsync(periodId.Value, "Demo: returneret til korrektion.", ct);
                    if (rjStatus == HttpStatusCode.OK) result.PeriodsRejected++;
                    else
                    {
                        result.PeriodOutcomeFailures++;
                        result.Warnings.Add($"reject {a.EmployeeId} → {(int)rjStatus} {Trunc(rjBody)}");
                    }
                    break;
                // "EMPLOYEE_APPROVED" → the send already put it there; leave as-is. (A legacy
                // manifest's "SUBMITTED" lands here too and means the same thing: no further act.)
            }
        }
        _log($"  absences={result.AbsencesSaved}, allocations={result.AllocationsSaved}, workDays={result.WorkDaysSaved}, " +
             $"monthsAlreadyComplete={result.MonthsAlreadyComplete}, sent={result.PeriodsSent}, " +
             $"alreadySent={result.PeriodsAlreadySent}, approved={result.PeriodsApproved}, " +
             $"rejected={result.PeriodsRejected}, outcomeFailures={result.PeriodOutcomeFailures}");
    }

    // ── Vikars: probe the GET first (idempotent), then create ──
    private async Task CreateVikarsAsync(LoadResult result, CancellationToken ct)
    {
        _log($"Creating {_manifest.Vikars.Count} vikar assignments ...");
        foreach (var v in _manifest.Vikars)
        {
            var (getStatus, getBody) = await _api.GetVikarAsync(v.ManagerId, ct);
            if (getStatus == HttpStatusCode.OK && VikarAlreadyActive(getBody))
            {
                result.VikarsSkipped++;
                continue;
            }

            var (status, body) = await _api.CreateVikarAsync(v.ManagerId, new
            {
                vikarUserId = v.VikarUserId,
                effectiveTo = v.EffectiveTo,
                reason = v.Reason,
            }, ct);
            if (status == HttpStatusCode.OK || status == HttpStatusCode.Created)
                result.VikarsCreated++;
            else if (status == HttpStatusCode.Conflict)
                result.VikarsSkipped++;
            else
                result.Warnings.Add($"vikar {v.ManagerId}->{v.VikarUserId} → {(int)status} {Trunc(body)}");
        }
        _log($"  vikars created={result.VikarsCreated}, skipped(existing)={result.VikarsSkipped}");
    }

    private static bool VikarAlreadyActive(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            // The GET returns the active vikar row when present; a null/empty vikar field ⇒ none.
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("vikarUserId", out var vu) && vu.ValueKind == JsonValueKind.String)
                    return true;
                if (root.TryGetProperty("activeVikar", out var av) && av.ValueKind == JsonValueKind.Object)
                    return true;
            }
            return false;
        }
        catch (JsonException) { return false; }
    }

    // ── Messy cases: most are FLAGS in the manifest (durable notes) the loader records as
    //    applied without further API calls; the cross-styrelse + agreement-change scripts would
    //    require destructive multi-step flows that are intentionally left as documented manual
    //    follow-ups (so re-running load never corrupts state). We count them as "present". ──
    private Task ApplyMessyCasesAsync(LoadResult result, CancellationToken ct)
    {
        result.MessyApplied = _manifest.MessyCases.Count;
        _log($"  messy cases present in manifest={result.MessyApplied} (scripted/flagged; destructive steps are documented manual follow-ups)");
        return Task.CompletedTask;
    }

    private static string Trunc(string s) => s.Length > 200 ? s[..200] + "…" : s;
}
