using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S70 / TASK-7002 (ADR-033 slice 3a, SPRINT-70 R1/R7a/R9/R10/R12) — the admin
/// employment-end-date endpoint + the same-tx deactivation lifecycle:
///
/// <list type="bullet">
///   <item><description>R1(a) set-past → same-tx flip + provenance + R1(e) side effects
///     (<c>ReportingLineManagerDeactivated</c>);</description></item>
///   <item><description>R1(b) future-dated → date stored, NO flip;</description></item>
///   <item><description>R1(c) clear → reactivation ONLY on lifecycle provenance;</description></item>
///   <item><description>R1(d) set on a manually-inactive user → no provenance claim;</description></item>
///   <item><description>R7a 409 on an active settlement for an affected ferieår — including the
///     R12 race pin (guard re-evaluated INSIDE the employee advisory lock);</description></item>
///   <item><description>If-Match/ADR-019 on ACTIVE and DEACTIVATED rows (terminated-aware reads);</description></item>
///   <item><description>R10 event payload + ADR-026 audit_projection row + users_audit row, one tx;</description></item>
///   <item><description>the R9e denial matrix extended to this NEW endpoint.</description></item>
/// </list>
///
/// <para>Fixture/JWT conventions mirror <see cref="Security.TerminatedEmployeeAccessTests"/>
/// (same WAF harness, token minting, direct <c>vacation_settlements</c> seeding). NOTE: the full
/// cross-party races (clear-vs-Step-A-flip, clear-vs-settle) land in TASK-7005 where Step A
/// exists — here the single-process R12 pin proves lock acquisition + in-lock guard ordering.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentEndDateLifecycleTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";        // target employees' org (/MIN01/STY01/)
    private const string DisjointOrg = "STY05";  // /MIN02/STY05/ — disjoint from STY01
    private const string CoveringOrg = "STY01";  // S93 flat role-scope: covers STY01 by exact ORG_ONLY match (a MAO no longer covers a child)
    private const string VacationType = "VACATION";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    // Real-clock anchored dates (the endpoint compares against the Copenhagen business date;
    // ±2-year margins make the past/future classification immune to the UTC/CET offset).
    private static readonly DateOnly TodayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly PastDate = TodayUtc.AddYears(-2);
    private static readonly DateOnly FutureDate = TodayUtc.AddYears(2);

    /// <summary>R6 ferieår resolution (VACATION reset_month = 9, uniform by DB CHECK).</summary>
    private static int FerieaarOf(DateOnly d) => d.Month >= 9 ? d.Year : d.Year - 1;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders (org tree MIN01/STY01/STY05 + configs)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // R1(a) — set-past: same-tx flip + provenance + side effects + event + audit.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>The R1(a) headline: setting an already-passed end date on an ACTIVE user
    /// deactivates same-tx with provenance, emits the R1(e) ReportingLineManagerDeactivated
    /// side effect for a line the leaver manages, emits EmployeeEmploymentEndDateSet on
    /// employee-{id}, and writes the ADR-026 audit_projection + users_audit rows — one tx.</summary>
    [Fact]
    public async Task SetPast_ActiveUser_FlipsWithProvenance_SideEffects_Event_Audit()
    {
        var leaver = await SeedEmployeeAsync();
        var report = await SeedEmployeeAsync();
        await SeedReportingLineAsync(employeeId: report, managerId: leaver);

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), leaver, PastDate, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.True(body.GetProperty("endDateDeactivated").GetBoolean());
        Assert.Equal(2L, body.GetProperty("version").GetInt64());
        Assert.Equal("\"2\"", rsp.Headers.ETag!.Tag);

        var tuple = await ReadEndDateTupleAsync(leaver);
        Assert.Equal(PastDate, tuple.EndDate);
        Assert.True(tuple.Provenance);
        Assert.False(tuple.IsActive);
        Assert.Equal(2L, tuple.Version);

        // R1(e) — the existing user-deactivation side-effect path fired in the SAME tx.
        var sideEffect = await ReadLatestOutboxPayloadAsync(
            streamId: $"reporting-line-{report}", eventType: "ReportingLineManagerDeactivated");
        Assert.NotNull(sideEffect);
        using (var doc = JsonDocument.Parse(sideEffect!))
        {
            Assert.Equal(leaver, doc.RootElement.GetProperty("managerId").GetString());
            Assert.Equal(report, doc.RootElement.GetProperty("employeeId").GetString());
        }

        // R10 — the lifecycle event on employee-{id} with the full transition payload.
        var payload = await ReadLatestOutboxPayloadAsync(
            streamId: $"employee-{leaver}", eventType: "EmployeeEmploymentEndDateSet");
        Assert.NotNull(payload);
        using (var doc = JsonDocument.Parse(payload!))
        {
            var root = doc.RootElement;
            Assert.Equal(leaver, root.GetProperty("employeeId").GetString());
            Assert.Equal(PastDate.ToString("yyyy-MM-dd"), root.GetProperty("newEndDate").GetString());
            Assert.True(root.GetProperty("oldIsActive").GetBoolean());
            Assert.False(root.GetProperty("newIsActive").GetBoolean());
            Assert.Equal(1L, root.GetProperty("versionBefore").GetInt64());
            Assert.Equal(2L, root.GetProperty("versionAfter").GetInt64());
            // Set (null → date): oldEndDate is null/absent under WhenWritingNull.
            Assert.False(root.TryGetProperty("oldEndDate", out var old) && old.ValueKind != JsonValueKind.Null);
        }

        // ADR-026 — audit_projection row present (TENANT_TARGETED on the leaver's org).
        var audit = await ReadAuditProjectionAsync(leaver);
        Assert.NotNull(audit);
        Assert.Equal("TENANT_TARGETED", audit!.Value.Scope);
        Assert.Equal(OrgId, audit.Value.TargetOrgId);

        // users_audit UPDATED row with the lifecycle tuple transition.
        var (versionBefore, versionAfter, newData) = await ReadLatestUsersAuditAsync(leaver);
        Assert.Equal(1L, versionBefore);
        Assert.Equal(2L, versionAfter);
        using (var doc = JsonDocument.Parse(newData))
        {
            Assert.False(doc.RootElement.GetProperty("isActive").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("endDateDeactivated").GetBoolean());
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // R1(b) — future-dated: store only, no flip.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetFuture_ActiveUser_StoresDate_NoFlip_NoProvenance()
    {
        var employeeId = await SeedEmployeeAsync();

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, FutureDate, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Equal(FutureDate, tuple.EndDate);
        Assert.False(tuple.Provenance);
        Assert.True(tuple.IsActive);   // the Step-A poller (TASK-7005) flips when the date passes
        Assert.Equal(2L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R1(c) — clear: provenance-guarded reactivation.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Clear after a lifecycle flip → reactivates + resets provenance. R1(f): this is
    /// the ONLY reactivation path for lifecycle-deactivated leavers (the admin general PUT
    /// filters is_active=TRUE) — pinned here.</summary>
    [Fact]
    public async Task Clear_AfterLifecycleFlip_Reactivates_AndResetsProvenance()
    {
        var employeeId = await SeedEmployeeAsync();
        await AssertOk(PutEndDateAsync(HrClient(CoveringOrg), employeeId, PastDate, ifMatch: "\"1\"")); // flip, v2

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, null, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.False(tuple.Provenance);
        Assert.True(tuple.IsActive);   // reactivated — the deactivation was end-date provenance
        Assert.Equal(3L, tuple.Version);
    }

    /// <summary>Clear on a MANUALLY-deactivated user clears the date but does NOT reactivate
    /// (the lifecycle must never blindly flip back a manual admin deactivation).</summary>
    [Fact]
    public async Task Clear_OnManuallyDeactivated_ClearsDate_DoesNotReactivate()
    {
        var employeeId = await SeedEmployeeAsync();
        await AssertOk(PutEndDateAsync(HrClient(CoveringOrg), employeeId, FutureDate, ifMatch: "\"1\"")); // v2, still active
        await ManuallyDeactivateAsync(employeeId); // direct flip, NO provenance (version untouched)

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, null, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.False(tuple.Provenance);
        Assert.False(tuple.IsActive);  // NOT reactivated — provenance was false
        Assert.Equal(3L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R1(d) — set on a manually-inactive user: record, no provenance claim, no change.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SetPast_OnManuallyInactiveUser_RecordsDate_NoProvenanceClaim()
    {
        var employeeId = await SeedEmployeeAsync();
        await ManuallyDeactivateAsync(employeeId);

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, PastDate, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Equal(PastDate, tuple.EndDate);
        Assert.False(tuple.Provenance);  // the deactivation remains MANUAL provenance
        Assert.False(tuple.IsActive);
        Assert.Equal(2L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Correction (date → date) — event old/new pair correct.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Correction_DateToDate_EventCarriesOldAndNewEndDates()
    {
        var employeeId = await SeedEmployeeAsync();
        var firstDate = FutureDate;
        var correctedDate = FutureDate.AddMonths(3);
        await AssertOk(PutEndDateAsync(HrClient(CoveringOrg), employeeId, firstDate, ifMatch: "\"1\""));

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, correctedDate, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var payload = await ReadLatestOutboxPayloadAsync(
            streamId: $"employee-{employeeId}", eventType: "EmployeeEmploymentEndDateSet");
        Assert.NotNull(payload);
        using var doc = JsonDocument.Parse(payload!);
        var root = doc.RootElement;
        Assert.Equal(firstDate.ToString("yyyy-MM-dd"), root.GetProperty("oldEndDate").GetString());
        Assert.Equal(correctedDate.ToString("yyyy-MM-dd"), root.GetProperty("newEndDate").GetString());
        Assert.True(root.GetProperty("oldIsActive").GetBoolean());
        Assert.True(root.GetProperty("newIsActive").GetBoolean());
        Assert.Equal(2L, root.GetProperty("versionBefore").GetInt64());
        Assert.Equal(3L, root.GetProperty("versionAfter").GetInt64());
    }

    // ════════════════════════════════════════════════════════════════════════
    // If-Match / ADR-019 — stale → 412, current → 200, on ACTIVE and DEACTIVATED rows.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IfMatch_ActiveRow_Stale412_Current200_Missing428()
    {
        var employeeId = await SeedEmployeeAsync();

        var stale = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, FutureDate, ifMatch: "\"99\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1L, staleBody.GetProperty("actualVersion").GetInt64());

        var missing = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, FutureDate, ifMatch: null);
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);

        var current = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, FutureDate, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    /// <summary>The terminated-aware read seam (R9a) end-to-end: a DEACTIVATED leaver's row is
    /// addressable — stale If-Match → 412 (carrying the ACTUAL version, never a false 404);
    /// current If-Match → 200. GET also serves the row + ETag.</summary>
    [Fact]
    public async Task IfMatch_DeactivatedRow_Stale412_Current200_GetServesEtag()
    {
        var employeeId = await SeedEmployeeAsync();
        await AssertOk(PutEndDateAsync(HrClient(CoveringOrg), employeeId, PastDate, ifMatch: "\"1\"")); // flip, v2

        var get = await HrClient(CoveringOrg).GetAsync(EndDateUrl(employeeId));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("\"2\"", get.Headers.ETag!.Tag);
        var getBody = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(getBody.GetProperty("isActive").GetBoolean());
        Assert.True(getBody.GetProperty("endDateDeactivated").GetBoolean());

        var stale = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, PastDate.AddDays(-1), ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2L, staleBody.GetProperty("actualVersion").GetInt64());

        var current = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, PastDate.AddDays(-1), ifMatch: "\"2\"");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Equal(PastDate.AddDays(-1), tuple.EndDate);
        Assert.True(tuple.Provenance);   // still-passed correction keeps the lifecycle state
        Assert.False(tuple.IsActive);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R7a — active settlement on an affected ferieår ⇒ 409 (set AND clear directions).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R7a_Set_ActiveSettlementOnNewDatesFerieaar_Returns409()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, FerieaarOf(PastDate), state: "SETTLED");

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, PastDate, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("R7a", body.GetProperty("error").GetString());
        Assert.Contains("3b", body.GetProperty("hint").GetString()); // reverse-then-re-settle is 3b
        Assert.Equal(FerieaarOf(PastDate),
            body.GetProperty("conflictingSettlement").GetProperty("entitlementYear").GetInt32());

        // Fail-closed: NOTHING mutated (no flip, no date, version unchanged).
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.True(tuple.IsActive);
        Assert.Equal(1L, tuple.Version);
    }

    /// <summary>Clear direction: the OLD end date's ferieår is affected too — a PENDING_REVIEW
    /// row (any non-REVERSED state counts as active) blocks the clear.</summary>
    [Fact]
    public async Task R7a_Clear_ActiveSettlementOnOldDatesFerieaar_Returns409()
    {
        var employeeId = await SeedEmployeeAsync();
        await AssertOk(PutEndDateAsync(HrClient(CoveringOrg), employeeId, FutureDate, ifMatch: "\"1\"")); // v2
        await SeedSettlementRowAsync(employeeId, FerieaarOf(FutureDate), state: "PENDING_REVIEW");

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, null, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Equal(FutureDate, tuple.EndDate); // unchanged
        Assert.Equal(2L, tuple.Version);
    }

    /// <summary>A REVERSED row is NOT active — it does not block the change.</summary>
    [Fact]
    public async Task R7a_ReversedSettlement_DoesNotBlock()
    {
        var employeeId = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(employeeId, FerieaarOf(FutureDate), state: "REVERSED");

        var rsp = await PutEndDateAsync(HrClient(CoveringOrg), employeeId, FutureDate, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 — the employee advisory lock is acquired FIRST and the R7a guard is
    // re-evaluated IN-LOCK (single-process pin; cross-party races land in TASK-7005).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>While a foreign tx holds the ADR-032 D4 employee advisory lock, the PUT blocks
    /// BEFORE its guards. A settlement row committed during that window MUST be seen by the
    /// in-lock R7a re-check → 409. If the endpoint evaluated the guard before acquiring the
    /// lock (or never took the lock), it would have seen an empty table and returned 200.</summary>
    [Fact]
    public async Task R12_GuardReevaluatedInsideAdvisoryLock_SettlementCommittedWhileBlocked_Yields409()
    {
        var employeeId = await SeedEmployeeAsync();

        await using var lockConn = new NpgsqlConnection(_harness.ConnectionString);
        await lockConn.OpenAsync();
        await using var lockTx = await lockConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", lockConn, lockTx))
        {
            lockCmd.Parameters.AddWithValue("employeeId", employeeId);
            await lockCmd.ExecuteScalarAsync();
        }

        // Fire the PUT — it must park on the advisory lock (NOT complete).
        var putTask = PutEndDateAsync(HrClient(CoveringOrg), employeeId, PastDate, ifMatch: "\"1\"");
        await Task.Delay(1500);
        Assert.False(putTask.IsCompleted,
            "PUT completed while the employee advisory lock was held by another tx — the endpoint is not acquiring the ADR-032 D4 lock.");

        // Commit an active settlement for the affected ferieår while the PUT is blocked.
        await SeedSettlementRowAsync(employeeId, FerieaarOf(PastDate), state: "SETTLED");

        // Release the lock — the PUT resumes, re-reads the row + re-evaluates R7a IN-LOCK.
        await lockTx.RollbackAsync();

        var rsp = await putTask;
        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Null(tuple.EndDate);
        Assert.True(tuple.IsActive); // fail-closed — no flip happened
    }

    // ════════════════════════════════════════════════════════════════════════
    // R9e — denial matrix extended to the NEW endpoint (4th allowlist surface).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Non-HR (Employee + LocalLeader) → 403 on GET and PUT (HROrAbove policy);
    /// the terminated target variant included.</summary>
    [Fact]
    public async Task EndDate_NonHr_Returns403()
    {
        var employeeId = await SeedEmployeeAsync();
        await ManuallyDeactivateAsync(employeeId); // terminated target

        var empGet = await ClientWith(EmployeeToken("emp_qa_ed", OrgId)).GetAsync(EndDateUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, empGet.StatusCode);

        var leaderGet = await ClientWith(LeaderToken("ldr_qa_ed", OrgId)).GetAsync(EndDateUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, leaderGet.StatusCode);

        var empPut = await PutEndDateAsync(ClientWith(EmployeeToken("emp_qa_ed2", OrgId)),
            employeeId, PastDate, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, empPut.StatusCode);

        var leaderPut = await PutEndDateAsync(ClientWith(LeaderToken("ldr_qa_ed2", OrgId)),
            employeeId, PastDate, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, leaderPut.StatusCode);
    }

    /// <summary>HR outside the target's org subtree → 403 on GET and PUT (subtree binding holds
    /// on the terminated-inclusive path; FAIL-001 cross-org guard).</summary>
    [Fact]
    public async Task EndDate_HrOutOfSubtree_Returns403()
    {
        var employeeId = await SeedEmployeeAsync();

        var get = await HrClient(DisjointOrg).GetAsync(EndDateUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        var put = await PutEndDateAsync(HrClient(DisjointOrg), employeeId, PastDate, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    /// <summary>A terminated employee's OWN still-valid JWT → 403 on the new endpoint (the
    /// HROrAbove policy excludes Employee tokens; the new validator additionally has no
    /// own-data branch — extends the R9e terminated-self pin to this surface).</summary>
    [Fact]
    public async Task EndDate_TerminatedSelf_Returns403()
    {
        var employeeId = await SeedEmployeeAsync();
        await AssertOk(PutEndDateAsync(HrClient(CoveringOrg), employeeId, PastDate, ifMatch: "\"1\"")); // lifecycle flip

        var get = await ClientWith(EmployeeToken(employeeId, OrgId)).GetAsync(EndDateUrl(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        var put = await PutEndDateAsync(ClientWith(EmployeeToken(employeeId, OrgId)),
            employeeId, null, ifMatch: "\"2\"");
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    /// <summary>In-subtree HR reaches a DEACTIVATED leaver on the new endpoint (the B2-class
    /// allowlist proof for this 4th surface) — and an unknown user is a clean 404.</summary>
    [Fact]
    public async Task EndDate_HrInScope_TerminatedTarget_Succeeds_Unknown404()
    {
        var employeeId = await SeedEmployeeAsync();
        await ManuallyDeactivateAsync(employeeId);

        var get = await HrClient(CoveringOrg).GetAsync(EndDateUrl(employeeId));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var unknown = await HrClient(CoveringOrg).GetAsync(EndDateUrl("no_such_user_s70_ed"));
        // The validator resolves the target first — unknown id is a 403 ("Target employee not
        // found" deny) per the established OrgScopeValidator convention.
        Assert.True(unknown.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden);
    }

    // ════════════════════════════════════════════════════════════════════════
    // S70 Step-7a W1 (internal Reviewer) — self-target exclusion on the end-date PUT: 403 for
    // ALL actors (active or terminated), BEFORE any DB work; a second administrator performs
    // legitimate self-departures. The GET stays self-readable.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>An ACTIVE in-subtree HR actor PUTting its OWN end date → 403, nothing mutated
    /// (deterministic, fail-closed — the rule is actor-state-independent).</summary>
    [Fact]
    public async Task EndDatePut_SelfTarget_ActiveHrActor_Returns403_NothingMutated()
    {
        var hrActor = await SeedEmployeeAsync(); // a real seeded user acting as HR on itself
        var selfClient = ClientWith(HrToken(hrActor, CoveringOrg));

        var rsp = await PutEndDateAsync(selfClient, hrActor, PastDate, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("second administrator", body.GetProperty("reason").GetString());

        var tuple = await ReadEndDateTupleAsync(hrActor);
        Assert.Null(tuple.EndDate);
        Assert.True(tuple.IsActive);
        Assert.False(tuple.Provenance);
        Assert.Equal(1L, tuple.Version);
    }

    /// <summary>The W1 attack choreography: a lifecycle-deactivated HR actor's STILL-VALID JWT
    /// (8h lifetime, no revocation) PUTs its own <c>employmentEndDate: null</c> — pre-W1 this
    /// permanently self-reinstated via the R1(c) reactivation. Must 403; the user stays
    /// deactivated. The GET stays self-readable (read-only).</summary>
    [Fact]
    public async Task EndDatePut_TerminatedSelf_HrActor_CannotSelfReinstate_403_StaysInactive()
    {
        var hrActor = await SeedEmployeeAsync();
        // A SECOND administrator flips the HR actor (the legitimate departure).
        await AssertOk(PutEndDateAsync(HrClient(CoveringOrg), hrActor, PastDate, ifMatch: "\"1\"")); // v2, inactive

        var selfClient = ClientWith(HrToken(hrActor, CoveringOrg)); // the still-valid own JWT

        // GET stays self-readable (the terminated-inclusive validator admits the HR scope).
        var get = await selfClient.GetAsync(EndDateUrl(hrActor));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        // The self-reinstatement attempt: PUT clear (null) with the current If-Match → 403.
        var put = await PutEndDateAsync(selfClient, hrActor, null, ifMatch: "\"2\"");
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        var body = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("second administrator", body.GetProperty("reason").GetString());

        // The user stays deactivated with the lifecycle state intact — no reactivation.
        var tuple = await ReadEndDateTupleAsync(hrActor);
        Assert.Equal(PastDate, tuple.EndDate);
        Assert.True(tuple.Provenance);
        Assert.False(tuple.IsActive);
        Assert.Equal(2L, tuple.Version);
    }

    // ─────────────────────────────── HTTP helpers ───────────────────────────────

    private static string EndDateUrl(string employeeId) =>
        $"/api/admin/employees/{employeeId}/employment-end-date";

    private static async Task<HttpResponseMessage> PutEndDateAsync(
        HttpClient client, string employeeId, DateOnly? endDate, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, EndDateUrl(employeeId))
        {
            Content = JsonContent.Create(new { employmentEndDate = endDate }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task AssertOk(Task<HttpResponseMessage> call)
    {
        var rsp = await call;
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    // ─────────────────────────────── clients / tokens ───────────────────────────────

    private HttpClient ClientWith(string bearer)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private HttpClient HrClient(string scopeOrgId) => ClientWith(HrToken("hr_s70_ed", scopeOrgId));

    private static string HrToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });
    }

    private static string EmployeeToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.Employee,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });
    }

    private static string LeaderToken(string actorId, string orgId)
    {
        var svc = new JwtTokenService(DevSettings());
        return svc.GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalLeader,
            agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") });
    }

    private static JwtSettings DevSettings() => new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    };

    // ─────────────────────────────── seeding / reads ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s70_ed_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Manual admin deactivation surrogate (matches the AdminEndpoints soft-delete
    /// semantic: is_active=FALSE, NO provenance, version untouched here for ETag simplicity).</summary>
    private async Task ManuallyDeactivateAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = FALSE, updated_at = NOW() WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedReportingLineAsync(string employeeId, string managerId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines
                (employee_id, manager_id, tree_root_org_id, relationship, effective_from, created_by)
            VALUES (@employeeId, @managerId, @treeRoot, 'PRIMARY', @from, 'test_s70_ed')
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("managerId", managerId);
        cmd.Parameters.AddWithValue("treeRoot", OrgId);
        cmd.Parameters.AddWithValue("from", new DateOnly(2024, 1, 1));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(DateOnly? EndDate, bool Provenance, bool IsActive, long Version)>
        ReadEndDateTupleAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employment_end_date, end_date_deactivated, is_active, version
            FROM users WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetInt64(3));
    }

    private async Task<string?> ReadLatestOutboxPayloadAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId AND event_type = @eventType
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("streamId", streamId);
        cmd.Parameters.AddWithValue("eventType", eventType);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private async Task<(string Scope, string? TargetOrgId)?> ReadAuditProjectionAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT visibility_scope, target_org_id FROM audit_projection
            WHERE event_type = 'EmployeeEmploymentEndDateSet' AND target_resource_id = @id
            ORDER BY occurred_at DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<(long VersionBefore, long VersionAfter, string NewData)>
        ReadLatestUsersAuditAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT version_before, version_after, new_data::text
            FROM users_audit
            WHERE user_id = @id AND action = 'UPDATED'
            ORDER BY audit_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "users_audit UPDATED row expected");
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2));
    }

    /// <summary>Direct <c>vacation_settlements</c> seed with a minimal valid snapshot — mirrors
    /// <see cref="Security.TerminatedEmployeeAccessTests"/> (trigger YEAR_END; the R7a guard is
    /// any-trigger, any-state-but-REVERSED).</summary>
    private async Task SeedSettlementRowAsync(string employeeId, int year, string state)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            transferAgreementDays = 0m,
            isFeriehindret = false,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version)
            VALUES
                (@e, @t, @y, 1, @state, 'YEAR_END', @snapshot::jsonb, 0, 0, 0, NULL, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        await cmd.ExecuteNonQueryAsync();
    }
}
