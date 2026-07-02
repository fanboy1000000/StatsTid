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
/// probes the GET first; activity submit is conflict-tolerant).
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
        public int PeriodsSubmitted { get; set; }
        public int PeriodsApproved { get; set; }
        public int PeriodsRejected { get; set; }
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

    // ── Activity: skema absences + a period transition ──
    private async Task CreateActivityAsync(LoadResult result, CancellationToken ct)
    {
        _log($"Creating activity for {_manifest.Activity.Count} employees ...");
        foreach (var a in _manifest.Activity)
        {
            var monthStart = new DateOnly(a.Year, a.Month, 1);
            var monthEnd = new DateOnly(a.Year, a.Month, DateTime.DaysInMonth(a.Year, a.Month));

            // 1. Save absences — but IDEMPOTENTLY: the skema save is event-sourced and APPENDS a
            //    fresh AbsenceRegistered per call (the absences_projection keys on event_id, not
            //    on (employee,date)), so a blind re-save would accumulate duplicate projection rows.
            //    Probe the month first and skip when absences are already present.
            if (a.Absences.Count > 0)
            {
                var (probeStatus, existingCount) = await _api.GetSkemaMonthAbsenceCountAsync(a.EmployeeId, a.Year, a.Month, ct);
                if (probeStatus == HttpStatusCode.OK && existingCount >= a.Absences.Count)
                {
                    // Already recorded on a prior run — idempotent skip.
                }
                else
                {
                    var (skStatus, skBody) = await _api.SkemaSaveAsync(a.EmployeeId, new
                    {
                        year = a.Year,
                        month = a.Month,
                        absences = a.Absences.Select(ab => new
                        {
                            date = ab.Date,
                            absenceType = ab.AbsenceType,
                            hours = ab.Hours,
                        }).ToList(),
                    }, ct);
                    if (skStatus == HttpStatusCode.OK)
                        result.AbsencesSaved += a.Absences.Count;
                    else if (skStatus == HttpStatusCode.Conflict)
                        { /* period already locked (APPROVED) on a prior run — idempotent skip */ }
                    else
                        result.Warnings.Add($"skema save {a.EmployeeId} {a.Year}-{a.Month} → {(int)skStatus} {Trunc(skBody)}");
                }
            }

            // 2. Period transition.
            if (a.PeriodOutcome == "NONE")
                continue;

            var (subStatus, periodId, subBody) = await _api.SubmitPeriodAsync(new
            {
                employeeId = a.EmployeeId,
                orgId = a.OrgId,
                periodStart = monthStart.ToString("yyyy-MM-dd"),
                periodEnd = monthEnd.ToString("yyyy-MM-dd"),
                periodType = "MONTHLY",
                agreementCode = a.AgreementCode,
                okVersion = a.OkVersion,
            }, ct);

            if (subStatus == HttpStatusCode.Conflict)
            {
                // Already SUBMITTED/APPROVED on a prior run — idempotent skip.
                continue;
            }
            if (subStatus != HttpStatusCode.OK || periodId is null)
            {
                result.Warnings.Add($"submit {a.EmployeeId} → {(int)subStatus} {Trunc(subBody)}");
                continue;
            }
            result.PeriodsSubmitted++;

            switch (a.PeriodOutcome)
            {
                case "APPROVED":
                    var (apStatus, apBody) = await _api.ApprovePeriodAsync(periodId.Value, ct);
                    if (apStatus == HttpStatusCode.OK) result.PeriodsApproved++;
                    else result.Warnings.Add($"approve {a.EmployeeId} → {(int)apStatus} {Trunc(apBody)}");
                    break;
                case "REJECTED":
                    var (rjStatus, rjBody) = await _api.RejectPeriodAsync(periodId.Value, "Demo: returneret til korrektion.", ct);
                    if (rjStatus == HttpStatusCode.OK) result.PeriodsRejected++;
                    else result.Warnings.Add($"reject {a.EmployeeId} → {(int)rjStatus} {Trunc(rjBody)}");
                    break;
                // "SUBMITTED" → leave as-is.
            }
        }
        _log($"  absences={result.AbsencesSaved}, submitted={result.PeriodsSubmitted}, approved={result.PeriodsApproved}, rejected={result.PeriodsRejected}");
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
