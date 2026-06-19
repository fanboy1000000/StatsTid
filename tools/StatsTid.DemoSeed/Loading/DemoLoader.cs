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
                .Where(e => e.TreeRootOrgId == tree.TreeRootOrgId)
                .ToList();
            _log($"Importing {edges.Count} edges for tree {tree.TreeRootOrgId} in batches of {_batchSize} ...");

            for (var offset = 0; offset < edges.Count; offset += _batchSize)
            {
                var batch = edges.Skip(offset).Take(_batchSize).ToList();
                var payload = new
                {
                    treeRootOrgId = tree.TreeRootOrgId,
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
                        $"Import batch failed for tree {tree.TreeRootOrgId} (offset {offset}): {(int)status} {body}");

                var (imported, skipped) = ParseImportCounts(body);
                result.EdgesImported += imported;
                result.EdgesSkipped += skipped;
            }
            _log($"  tree {tree.TreeRootOrgId}: imported so far={result.EdgesImported}, skipped={result.EdgesSkipped}");
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
