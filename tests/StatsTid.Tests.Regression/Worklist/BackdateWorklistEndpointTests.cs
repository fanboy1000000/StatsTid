using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Worklist;

/// <summary>
/// S138 / TASK-13803 — Docker-gated HTTP pins for the two HR backdate-worklist endpoints
/// (<c>GET /api/hr/backdate-worklist</c>, <c>POST /api/hr/backdate-worklist/{id}/resolve</c>).
/// RED-FIRST from the refinement spec (rev 4.2, TASK-13803 ACs): HR-only (HROrAbove policy + the
/// LocalHR per-scope floor bound to the ROW's employee, terminated-inclusive); the org-wide listing
/// filtered by the actor's HR accessible-org set; typed rows carrying keys, triggers, the derived
/// fields and <c>version</c> as ETag; resolve = If-Match REQUIRED (428 missing / 412 stale),
/// 404 unknown, 403 foreign HR, 409 already resolved, 422 bad verb / blank reason; 200 + new ETag.
///
/// <para>S140 / TASK-14010 adds the owner ruling OQ-7 (a) pins: RECALCULATED on an EXPORTED_MONTH
/// row is GLOBAL-ADMIN ONLY in the BACKEND (wave 2 found it enforced only by a hidden button), while
/// DISMISSED stays open to HR for both kinds and RECALCULATED stays open to HR for a SETTLED_YEAR
/// row.</para>
///
/// <para>HTTP-level via <see cref="StatsTidWebApplicationFactory"/> + <c>CreateClient()</c>; JWT
/// minting via the dev-fallback signing key (the AdminUserVersioningTests helper shape).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class BackdateWorklistEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string EmployeeOrg = "STY_WL_EP1";
    private const string ForeignOrg = "STY_WL_EP_FOREIGN";
    private const string Employee = "wl_ep_emp1";
    private const string ScopedHrActor = "wl_ep_hr_scoped";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private Guid _openRowId;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boots the host (seeders run)

        // Subject: an employee in EmployeeOrg with ONE exported month (March 2026) and ONE open
        // EXPORTED_MONTH worklist row raised by a PROFILE_CHANGE effective 15 Mar (strictly inside
        // the month → QUAL-149 visible). Also seed the foreign org so a foreign-HR scope is real.
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, Employee, EmployeeOrg);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, "wl_ep_foreign_emp", ForeignOrg);
        await ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            VALUES (@p0, NULL, @p1, 2026, 3, '[]'::jsonb, '[]'::jsonb, 'h-mar')
            """, Guid.NewGuid(), Employee);

        var repo = _factory.Services.GetRequiredService<HrBackdateWorklistRepository>();
        var dbFactory = _factory.Services.GetRequiredService<DbConnectionFactory>();
        await using var conn = dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var ids = await repo.WriteForExportedMonthsAsync(conn, tx, Employee,
            new WorklistTrigger(WorklistTriggerKinds.ProfileChange, Guid.NewGuid(), new DateOnly(2026, 3, 15), "hr_seed"),
            new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 1), CancellationToken.None);
        await tx.CommitAsync();
        _openRowId = Assert.Single(ids);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET — per-employee read
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_WithEmployeeId_GlobalAdmin_ReturnsTypedRow_WithKeysTriggersDerivedFieldsAndVersion()
    {
        var client = Client(GlobalAdminToken());
        var rsp = await client.GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind); // BARE array, not an envelope
        var row = Assert.Single(doc.RootElement.EnumerateArray());

        Assert.Equal(_openRowId, row.GetProperty("worklistId").GetGuid());
        Assert.Equal(Employee, row.GetProperty("employeeId").GetString());
        Assert.Equal("EXPORTED_MONTH", row.GetProperty("kind").GetString());
        Assert.Equal(2026, row.GetProperty("year").GetInt32());
        Assert.Equal(3, row.GetProperty("month").GetInt32());
        Assert.NotEqual(Guid.Empty, row.GetProperty("exportId").GetGuid());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("entitlementType").ValueKind);
        Assert.Equal(1L, row.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("resolvedAt").ValueKind);
        // S144 / TASK-14401 (3c): the wire property exists on an OPEN row and is JSON null (the
        // stamp is NULL iff the row is open). Red conditions: drop ResolutionBlockedBy from
        // ToDto → GetProperty throws KeyNotFoundException; serialise the open row's stamp as []
        // (or omit it) → the ValueKind assertion (or GetProperty) fails.
        Assert.Equal(JsonValueKind.Null, row.GetProperty("resolutionBlockedBy").ValueKind);

        var trigger =Assert.Single(row.GetProperty("triggers").EnumerateArray());
        Assert.Equal("PROFILE_CHANGE", trigger.GetProperty("kind").GetString());
        Assert.Equal("2026-03-15", trigger.GetProperty("effectiveFrom").GetString()); // HR-only surface — may be carried
        Assert.Equal("h-mar", trigger.GetProperty("baselineContentHash").GetString());
        Assert.False(trigger.GetProperty("recalculatedSince").GetBoolean());
        Assert.Equal(JsonValueKind.Null, trigger.GetProperty("reversedSince").ValueKind);
        Assert.Equal("QUAL-149", trigger.GetProperty("recalcBlockedBy").GetString());

        Assert.Equal(new[] { "QUAL-149" }, row.GetProperty("recalcBlockedBy").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.False(row.GetProperty("recalculatedSince").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("reversedSince").ValueKind);
    }

    [Fact]
    public async Task Get_WithEmployeeId_ScopedHr_Own_200_Foreign_403_Employee_403_Anonymous_401()
    {
        var own = await Client(HrToken(EmployeeOrg, ScopedHrActor)).GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        var foreign = await Client(HrToken(ForeignOrg, "wl_ep_hr_foreign")).GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        // An Employee-role token is denied by the HROrAbove policy — no employee-facing exposure.
        var employee = await Client(EmployeeToken(Employee, EmployeeOrg)).GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);

        var anonymous = await _factory.CreateClient().GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET — org-wide listing
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_OrgWide_ScopedHr_SeesOnlyOwnOrgRows_ForeignHr_EmptyList_GlobalAdmin_All()
    {
        using var ownDoc = JsonDocument.Parse(await Client(HrToken(EmployeeOrg, ScopedHrActor)).GetStringAsync("/api/hr/backdate-worklist"));
        Assert.Equal(new[] { Employee }, ownDoc.RootElement.EnumerateArray().Select(r => r.GetProperty("employeeId").GetString()).ToArray());

        // A foreign HR has a REAL (non-empty) accessible-org set → 200 with an EMPTY list, never
        // another org's rows.
        var foreignRsp = await Client(HrToken(ForeignOrg, "wl_ep_hr_foreign")).GetAsync("/api/hr/backdate-worklist");
        Assert.Equal(HttpStatusCode.OK, foreignRsp.StatusCode);
        using var foreignDoc = JsonDocument.Parse(await foreignRsp.Content.ReadAsStringAsync());
        Assert.Empty(foreignDoc.RootElement.EnumerateArray());

        using var adminDoc = JsonDocument.Parse(await Client(GlobalAdminToken()).GetStringAsync("/api/hr/backdate-worklist"));
        Assert.Contains(adminDoc.RootElement.EnumerateArray(), r => r.GetProperty("employeeId").GetString() == Employee);
    }

    // ════════════════════════════════════════════════════════════════════════
    // POST resolve — the ADR-019 If-Match contract + scope + 409/422
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Resolve_MissingIfMatch_428_Stale_412_Fresh_200WithNewEtag_Repeat_409_OpenFilterHonoured()
    {
        var client = Client(HrToken(EmployeeOrg, ScopedHrActor));
        var url = $"/api/hr/backdate-worklist/{_openRowId}/resolve";
        // S140 / TASK-14010 — this ladder pins the CONCURRENCY contract, so it uses the verb an HR
        // actor is permitted on an EXPORTED_MONTH row: DISMISSED. It previously used RECALCULATED,
        // which the OQ-7 (a) gate now (correctly) refuses to an HR actor with a 403 — the verb was
        // incidental to what this test pins, and both RECALCULATED paths are covered by the two
        // OQ-7 pins further down.
        var body = new { resolution = "DISMISSED", reason = "Not payroll-relevant after review" };

        // (1) No If-Match → 428 Precondition Required.
        var noHeader = await client.PostAsJsonAsync(url, body);
        Assert.Equal((HttpStatusCode)428, noHeader.StatusCode);

        // (2) Stale If-Match → 412 with the structured expected/actual body; row untouched.
        var stale = await SendResolveAsync(client, url, "\"99\"", body);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using (var staleDoc = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
        {
            Assert.Equal(99L, staleDoc.RootElement.GetProperty("expectedVersion").GetInt64());
            Assert.Equal(1L, staleDoc.RootElement.GetProperty("actualVersion").GetInt64());
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NOT NULL", _openRowId));

        // (3) Fresh If-Match "1" → 200, ETag "2", typed body.
        var ok = await SendResolveAsync(client, url, "\"1\"", body);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("\"2\"", ok.Headers.ETag!.Tag);
        using (var okDoc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()))
        {
            Assert.Equal(_openRowId, okDoc.RootElement.GetProperty("worklistId").GetGuid());
            Assert.Equal(Employee, okDoc.RootElement.GetProperty("employeeId").GetString());
            Assert.Equal("DISMISSED", okDoc.RootElement.GetProperty("resolution").GetString());
            Assert.Equal(2L, okDoc.RootElement.GetProperty("version").GetInt64());
            Assert.NotEqual(JsonValueKind.Null, okDoc.RootElement.GetProperty("resolvedAt").ValueKind);
        }
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_by = @p1 AND resolution = 'DISMISSED' AND version = 2", _openRowId, ScopedHrActor));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0", Employee));

        // S144 / TASK-14401 (3a): DISMISSED is stamped too — with the block set that held at the
        // moment of resolution (the setup row IS blocked by QUAL-149), not merely a non-NULL value.
        // Red conditions: stamp '{}' (or NULL, which the widened CHECK would also refuse) instead of
        // the derived set → the row count is 0; drop blockedBy from the event → the payload count is
        // 0; drop it from the audit mapper's details → the details count is 0; drop
        // ResolutionBlockedBy from ToDto / the repository read → the open=false GetProperty below
        // throws or the array differs.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution_blocked_by = '{QUAL-149}'", _openRowId));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved' AND event_payload -> 'blockedBy' = '[\"QUAL-149\"]'::jsonb", $"employee-{Employee}"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0 AND details -> 'blockedBy' = '[\"QUAL-149\"]'::jsonb", Employee));

        // (4) open=true (default) no longer lists it; open=false does, with the resolution fields.
        using (var openDoc = JsonDocument.Parse(await client.GetStringAsync($"/api/hr/backdate-worklist?employeeId={Employee}")))
            Assert.Empty(openDoc.RootElement.EnumerateArray());
        using (var allDoc = JsonDocument.Parse(await client.GetStringAsync($"/api/hr/backdate-worklist?employeeId={Employee}&open=false")))
        {
            var row = Assert.Single(allDoc.RootElement.EnumerateArray());
            Assert.Equal("DISMISSED", row.GetProperty("resolution").GetString());
            Assert.Equal(ScopedHrActor, row.GetProperty("resolvedBy").GetString());
            Assert.Equal(2L, row.GetProperty("version").GetInt64());
            Assert.Equal(new[] { "QUAL-149" }, row.GetProperty("resolutionBlockedBy").EnumerateArray().Select(e => e.GetString()).ToArray());
        }

        // (5) Resolving again with the FRESH token → 409 (already resolved, not a silent re-write).
        var again = await SendResolveAsync(client, url, "\"2\"", new { resolution = "DISMISSED", reason = "again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Resolve_ForeignHr_403_Employee_403_UnknownId_404_BadVerb_422_BlankReason_422()
    {
        var url = $"/api/hr/backdate-worklist/{_openRowId}/resolve";
        var good = new { resolution = "DISMISSED", reason = "Not payroll-relevant" };

        var foreign = await SendResolveAsync(Client(HrToken(ForeignOrg, "wl_ep_hr_foreign")), url, "\"1\"", good);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        var employee = await SendResolveAsync(Client(EmployeeToken(Employee, EmployeeOrg)), url, "\"1\"", good);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);

        var hr = Client(HrToken(EmployeeOrg, ScopedHrActor));

        var unknown = await SendResolveAsync(hr, $"/api/hr/backdate-worklist/{Guid.NewGuid()}/resolve", "\"1\"", good);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var badVerb = await SendResolveAsync(hr, url, "\"1\"", new { resolution = "IGNORED", reason = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badVerb.StatusCode);

        var blankReason = await SendResolveAsync(hr, url, "\"1\"", new { resolution = "DISMISSED", reason = "   " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blankReason.StatusCode);

        // None of the refusals touched the row.
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND version = 1", _openRowId));
    }

    // ════════════════════════════════════════════════════════════════════════
    // POST resolve — S140 / TASK-14010, owner ruling OQ-7 (a): the RECALCULATED
    // verb on an EXPORTED_MONTH row is GLOBAL-ADMIN ONLY, in the BACKEND
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The gate tracks the REMEDY. An EXPORTED_MONTH row is fixed by <c>POST /api/payroll/recalculate</c>,
    /// which is <c>GlobalAdminOnly</c>, so recording "Recalculated" on such a row asserts an act only
    /// a Global Admin may perform. Wave 2 found the rule implemented ONLY in the screen (the button
    /// is hidden), which is not a gate at all: an HR user could POST it directly. This pins the
    /// SERVER-side refusal, and pins that it is the ROLE that decides — the identical request
    /// succeeds for a Global Admin.
    ///
    /// <para><b>Red conditions.</b> (1) Delete the in-handler gate in
    /// <c>BackdateWorklistEndpoints</c> → the HR and LocalAdmin calls return 200 and the
    /// row-untouched assertions fail. (2) Loosen it to a role FLOOR (e.g.
    /// <c>IsAtLeast(role, LocalAdmin)</c>) → the LocalAdmin leg returns 200 and fails. (3) Make it
    /// unconditional (refuse every actor, or ignore the actor's role) → the Global-Admin leg 403s
    /// and fails. (4) Move the gate BEFORE the org-scope validation → the foreign-HR leg's 403
    /// reason changes from the scope reason to the rule reason and fails, which is the pin that the
    /// refusal is not an existence/kind oracle for a row the caller may not see.</para>
    /// </summary>
    [Fact]
    public async Task Resolve_ExportedMonth_AsRecalculated_Hr403_LocalAdmin403_GlobalAdmin409Blocked_ForeignHrStillScope403()
    {
        var url = $"/api/hr/backdate-worklist/{_openRowId}/resolve";
        var recalculated = new { resolution = "RECALCULATED", reason = "Re-planned March 2026 via /api/payroll/recalculate" };

        // (1) An in-scope HR actor — passes HROrAbove, passes the org-scope check, and is REFUSED
        // by the OQ-7 (a) rule. This is the request the hidden button used to be the only guard on.
        var hr = await SendResolveAsync(Client(HrToken(EmployeeOrg, ScopedHrActor)), url, "\"1\"", recalculated);
        Assert.Equal(HttpStatusCode.Forbidden, hr.StatusCode);
        using (var hrDoc = JsonDocument.Parse(await hr.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Access denied", hrDoc.RootElement.GetProperty("error").GetString());
            var reason = hrDoc.RootElement.GetProperty("reason").GetString() ?? string.Empty;
            // The reason names the RULE (and the verb HR may use instead), not the row.
            Assert.Contains("GlobalAdmin", reason, StringComparison.Ordinal);
            Assert.Contains("RECALCULATED", reason, StringComparison.Ordinal);
            Assert.DoesNotContain(Employee, reason, StringComparison.Ordinal);
        }

        // (2) A LocalAdmin is ABOVE HR and still not a Global Admin → also refused. This is what
        // discriminates the ruled gate from a mere "HR is refused" or a role-floor implementation.
        var localAdmin = await SendResolveAsync(Client(LocalAdminToken(EmployeeOrg, "wl_ep_ladmin")), url, "\"1\"", recalculated);
        Assert.Equal(HttpStatusCode.Forbidden, localAdmin.StatusCode);

        // (3) A FOREIGN HR gets the SCOPE 403, not the rule 403 — so the new refusal can never
        // double as an existence (or kind) oracle for a row outside the caller's scope.
        var foreign = await SendResolveAsync(Client(HrToken(ForeignOrg, "wl_ep_hr_foreign")), url, "\"1\"", recalculated);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using (var foreignDoc = JsonDocument.Parse(await foreign.Content.ReadAsStringAsync()))
        {
            var reason = foreignDoc.RootElement.GetProperty("reason").GetString() ?? string.Empty;
            // The property that matters: an out-of-scope caller learns NOTHING about the row's kind
            // or about the OQ-7 rule — they get the same scope refusal they would get for any verb.
            Assert.DoesNotContain("GlobalAdmin", reason, StringComparison.Ordinal);
            Assert.Equal("Actor scope does not cover target organization", reason);
        }

        // None of the three refusals touched the row, emitted an event, or wrote an audit row —
        // a 403 must be a refusal, not a write with a bad status code.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND version = 1", _openRowId));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0", Employee));

        // (4) S144 — the SAME request, differing ONLY in the actor's role, now reaches the NEXT
        // gate for a Global Admin: the setup row is BLOCKED (trigger 15 Mar, strictly inside the
        // month → QUAL-149), so RECALCULATED is refused with 409 worklist-recalc-blocked and NOTHING
        // is written. This leg keeps its purpose: a 409-blocked is reachable ONLY past the role
        // gate (a 403 actor never gets a block verdict), so it still proves the 403s above were
        // the rule biting and not a malformed request; the gate does not over-block the one role
        // that CAN act — it lets it through to the block check. (The positive Global-Admin 200 is
        // the separate fact ..._TriggerOnFirstOfMonth_... below.)
        var admin = await SendResolveAsync(Client(GlobalAdminToken()), url, "\"1\"", recalculated);
        Assert.Equal(HttpStatusCode.Conflict, admin.StatusCode);
        using (var blockedDoc = JsonDocument.Parse(await admin.Content.ReadAsStringAsync()))
        {
            Assert.Equal("worklist-recalc-blocked", blockedDoc.RootElement.GetProperty("kind").GetString());
            Assert.Equal(new[] { "QUAL-149" }, blockedDoc.RootElement.GetProperty("blockedBy").EnumerateArray().Select(e => e.GetString()).ToArray());
            Assert.False(string.IsNullOrWhiteSpace(blockedDoc.RootElement.GetProperty("error").GetString()));
        }
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND resolution_blocked_by IS NULL AND version = 1", _openRowId));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0", Employee));
    }

    /// <summary>
    /// S144 / TASK-14401 leg (5), a SEPARATE fact (a test stops at its first failed assertion, so a
    /// leg after the deliberately-red 409 leg would never run in evidence run 1). The trigger is
    /// effective on the FIRST of April 2026, so the correction does not reach into the exported month
    /// (QUAL-149 needs a date strictly inside it) → the row is UNBLOCKED → a Global Admin RECALCULATED
    /// succeeds, and the resolution is stamped with the EMPTY set (not NULL: NULL means open).
    ///
    /// <para><b>Red conditions.</b> (1) Refuse RECALCULATED for a Global Admin unconditionally, or
    /// treat a 1st-of-month trigger as blocked (<c>&gt;=</c> in <c>IsStrictlyInsideMonth</c>) → 409,
    /// not 200. (2) Stamp NULL (the widened CHECK refuses it, so 500) or a non-empty set → the
    /// <c>resolution_blocked_by = '{}'</c> count is 0. (3) Omit <c>blockedBy</c> from the event, or
    /// emit <c>null</c> instead of <c>[]</c> → the event assertion's <c>ValueKind</c>/emptiness fails.</para>
    /// </summary>
    [Fact]
    public async Task Resolve_ExportedMonth_TriggerOnFirstOfMonth_AsRecalculated_GlobalAdmin200_StampsEmptySet()
    {
        await ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            VALUES (@p0, NULL, @p1, 2026, 4, '[]'::jsonb, '[]'::jsonb, 'h-apr')
            """, Guid.NewGuid(), Employee);

        Guid aprilRowId;
        {
            var repo = _factory.Services.GetRequiredService<HrBackdateWorklistRepository>();
            var dbFactory = _factory.Services.GetRequiredService<DbConnectionFactory>();
            await using var conn = dbFactory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var ids = await repo.WriteForExportedMonthsAsync(conn, tx, Employee,
                new WorklistTrigger(WorklistTriggerKinds.ProfileChange, Guid.NewGuid(), new DateOnly(2026, 4, 1), "hr_seed"),
                new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), CancellationToken.None);
            await tx.CommitAsync();
            aprilRowId = ids.Single(); // only April: the March row was raised by the setup, not this call
        }

        var rsp = await SendResolveAsync(Client(GlobalAdminToken()),
            $"/api/hr/backdate-worklist/{aprilRowId}/resolve", "\"1\"",
            new { resolution = "RECALCULATED", reason = "Re-planned April 2026 via /api/payroll/recalculate" });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("\"2\"", rsp.Headers.ETag!.Tag);

        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution = 'RECALCULATED' AND resolved_by = 'wl_ep_admin' AND version = 2 AND resolution_blocked_by = '{}'", aprilRowId));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved' AND event_payload ->> 'worklistId' = @p1 AND event_payload -> 'blockedBy' = '[]'::jsonb",
            $"employee-{Employee}", aprilRowId.ToString()));
    }

    /// <summary>
    /// S144 / TASK-14401 — precedence: a STALE If-Match outranks the block verdict. A Global Admin
    /// sends RECALCULATED on the BLOCKED row with the stale token <c>"99"</c>: the answer is 412
    /// (the caller's copy is out of date, so any block verdict about it would be about a row they
    /// are not looking at), never 409-blocked. The 412 comes from the repository's version guard via
    /// the handler's <c>OptimisticConcurrencyException</c> catch.
    ///
    /// <para><b>Red conditions.</b> Move the block check before the version guard (in the handler or
    /// the repository) → the response is 409 <c>worklist-recalc-blocked</c>, not 412, and the status
    /// assertion fails. Drop the block check's precedence differently — e.g. return 409 for a stale
    /// token on any blocked row — same failure.</para>
    /// </summary>
    [Fact]
    public async Task Resolve_ExportedMonth_Blocked_AsRecalculated_StaleIfMatch_Is412_Not409()
    {
        var rsp = await SendResolveAsync(Client(GlobalAdminToken()),
            $"/api/hr/backdate-worklist/{_openRowId}/resolve", "\"99\"",
            new { resolution = "RECALCULATED", reason = "Stale token on a blocked row" });
        Assert.Equal(HttpStatusCode.PreconditionFailed, rsp.StatusCode);
        using (var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync()))
        {
            Assert.Equal(99L, doc.RootElement.GetProperty("expectedVersion").GetInt64());
            Assert.Equal(1L, doc.RootElement.GetProperty("actualVersion").GetInt64());
        }
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND version = 1", _openRowId));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));
    }

    /// <summary>
    /// S144 / TASK-14401 — the new verb <c>HANDLED_MANUALLY</c> on an EXPORTED_MONTH row is gated
    /// exactly like RECALCULATED (it asserts an act only a Global Admin performs: the payroll
    /// correction was handled outside the tool): HR 403, LocalAdmin 403, Global Admin 200. The 200
    /// stamps the block set that held at resolution (the setup row is blocked by QUAL-149) on the
    /// row, the event, the audit row and the wire.
    ///
    /// <para><b>Red conditions.</b> (1) Leave HANDLED_MANUALLY out of the verb whitelist → the
    /// Global-Admin leg is 422, not 200. (2) Gate it as HR-open (no OQ-7 (a) extension to the new
    /// verb) → the HR leg returns 200 (and the row-untouched counts fail). (3) Gate it as a role
    /// FLOOR → the LocalAdmin leg is 200. (4) Also block it on QUAL-149 like RECALCULATED → the
    /// Global-Admin leg is 409 (the whole point of the verb is that it is the way out of a block).
    /// (5) Stamp <c>{}</c> instead of the derived set → the <c>'{QUAL-149}'</c> and event
    /// assertions trip. (6) Drop blockedBy from the audit mapper → the <c>details</c> assertion
    /// trips. (7) Drop ResolutionBlockedBy from <c>ToDto</c> → the read-back <c>GetProperty</c> throws.
    /// (8) Reword the 403 so it omits <c>GlobalAdmin</c>/<c>RECALCULATED</c> or names the employee →
    /// the reason assertions trip.</para>
    /// </summary>
    [Fact]
    public async Task Resolve_ExportedMonth_AsHandledManually_Hr403_LocalAdmin403_GlobalAdmin200_StampsBlockSet()
    {
        var url = $"/api/hr/backdate-worklist/{_openRowId}/resolve";
        var handled = new { resolution = "HANDLED_MANUALLY", reason = "Paid out by hand in the payroll system, March 2026" };

        var hr = await SendResolveAsync(Client(HrToken(EmployeeOrg, ScopedHrActor)), url, "\"1\"", handled);
        Assert.Equal(HttpStatusCode.Forbidden, hr.StatusCode);
        using (var hrDoc = JsonDocument.Parse(await hr.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Access denied", hrDoc.RootElement.GetProperty("error").GetString());
            var reason = hrDoc.RootElement.GetProperty("reason").GetString() ?? string.Empty;
            Assert.Contains("GlobalAdmin", reason, StringComparison.Ordinal);
            Assert.Contains("RECALCULATED", reason, StringComparison.Ordinal); // the reason still names the gated verb
            Assert.DoesNotContain(Employee, reason, StringComparison.Ordinal);
        }

        var localAdmin = await SendResolveAsync(Client(LocalAdminToken(EmployeeOrg, "wl_ep_ladmin")), url, "\"1\"", handled);
        Assert.Equal(HttpStatusCode.Forbidden, localAdmin.StatusCode);

        // Refusals wrote nothing.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND version = 1", _openRowId));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0", Employee));

        var admin = await SendResolveAsync(Client(GlobalAdminToken()), url, "\"1\"", handled);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("\"2\"", admin.Headers.ETag!.Tag);

        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution = 'HANDLED_MANUALLY' AND resolved_by = 'wl_ep_admin' AND version = 2 AND resolution_blocked_by = '{QUAL-149}'", _openRowId));

        // The event: the resolution and the block set; and the row's stamp equals the event's.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved' AND event_payload ->> 'resolution' = 'HANDLED_MANUALLY' AND event_payload -> 'blockedBy' = '[\"QUAL-149\"]'::jsonb", $"employee-{Employee}"));

        // The audit row: resolution, reason and blockedBy in details.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0 AND details ->> 'resolution' = 'HANDLED_MANUALLY' AND details ->> 'reason' = 'Paid out by hand in the payroll system, March 2026' AND details -> 'blockedBy' = '[\"QUAL-149\"]'::jsonb", Employee));

        // The wire: a resolved row's resolutionBlockedBy is the array.
        using var allDoc = JsonDocument.Parse(await Client(GlobalAdminToken()).GetStringAsync($"/api/hr/backdate-worklist?employeeId={Employee}&open=false"));
        var row = Assert.Single(allDoc.RootElement.EnumerateArray());
        Assert.Equal("HANDLED_MANUALLY", row.GetProperty("resolution").GetString());
        Assert.Equal(new[] { "QUAL-149" }, row.GetProperty("resolutionBlockedBy").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>
    /// S144 / TASK-14401 — the OQ-7 (a) gate is about the EXPORTED_MONTH remedy; a SETTLED_YEAR row
    /// stays HR-open for every verb, including the new one. An in-scope HR actor records
    /// HANDLED_MANUALLY on a SETTLED_YEAR row → 200, stamped with the EMPTY set (a settled-year row
    /// is never blocked; the QUAL-149 block is an EXPORTED_MONTH-only concept).
    ///
    /// <para><b>Red conditions.</b> (1) Apply the Global-Admin gate to HANDLED_MANUALLY regardless
    /// of <c>row.Kind</c> → the HR call is 403, not 200. (2) Stamp NULL → the widened CHECK refuses
    /// (500), or, if the CHECK were absent, the <c>'{}'</c> count is 0. (3) Treat a SETTLED_YEAR row
    /// as blocked → the stamp is non-empty and the event's <c>blockedBy</c> assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task Resolve_SettledYear_AsHandledManually_Hr200_StampsEmptySet()
    {
        var settledRowId = await SeedSettledYearRowAsync();
        var rsp = await SendResolveAsync(Client(HrToken(EmployeeOrg, ScopedHrActor)),
            $"/api/hr/backdate-worklist/{settledRowId}/resolve", "\"1\"",
            new { resolution = "HANDLED_MANUALLY", reason = "Settled ferieår 2025 corrected by hand" });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        using (var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("HANDLED_MANUALLY", doc.RootElement.GetProperty("resolution").GetString());
            var stamp = doc.RootElement.GetProperty("resolutionBlockedBy");
            Assert.Equal(JsonValueKind.Array, stamp.ValueKind);
            Assert.Empty(stamp.EnumerateArray());
        }
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND kind = 'SETTLED_YEAR' AND resolution = 'HANDLED_MANUALLY' AND resolved_by = @p1 AND resolution_blocked_by = '{}'", settledRowId, ScopedHrActor));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved' AND event_payload ->> 'worklistId' = @p1 AND event_payload -> 'blockedBy' = '[]'::jsonb",
            $"employee-{Employee}", settledRowId.ToString()));
    }

    /// <summary>
    /// S140 sprint-end review — the MIXED-ROLE CLAIM SHAPE, which is the shape the three pins above
    /// do not cover and the shape the defect survived behind (the SEC-021 over-grant family).
    ///
    /// <para><b>Plain language.</b> A token can carry one role in its <c>role</c> claim and a
    /// DIFFERENT role inside its <c>scopes</c> array. This actor's primary role is LocalHR, but it
    /// holds a GLOBAL scope stamped <c>Role = GlobalAdmin</c>. The <c>GlobalAdminOnly</c> policy —
    /// and therefore the remedy, <c>POST /api/payroll/recalculate</c> — decides on the PRIMARY ROLE
    /// CLAIM ALONE (<c>requireOrgScope: false</c> makes <see cref="ScopeAuthorizationHandler"/>
    /// succeed or fail on that claim and return without reading <c>scopes</c>), so this actor is
    /// REFUSED the recalculation. An earlier revision of the in-handler gate accepted the GLOBAL
    /// GlobalAdmin scope as a fallback signal, so this actor was ADMITTED here: it could record
    /// "this exported payroll month has been recalculated" as audited fact, be unable to perform
    /// the recalculation, and take the row off HR's open list with nobody chasing it. The gate must
    /// be no looser than the endpoint it mirrors.</para>
    ///
    /// <para><b>Why the token still reaches the gate</b> (this is what makes the pin sharp rather
    /// than vacuous): <c>HROrAbove</c> passes on the LocalHR role claim, and
    /// <c>OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync</c> admits via its
    /// <c>ScopeType == "GLOBAL"</c> branch (the GlobalAdmin scope clears the LocalHR role floor).
    /// So the ONLY thing that can refuse this request is the OQ-7 (a) gate — and leg (b) proves the
    /// token is genuinely able to resolve this very row, by DISMISSING it successfully.</para>
    ///
    /// <para><b>Red condition (the one that matters).</b> Restore the scope fallback in
    /// <c>BackdateWorklistEndpoints.IsGlobalAdmin</c> — i.e. also return true when any scope has
    /// <c>Role == GlobalAdmin &amp;&amp; ScopeType == "GLOBAL"</c> — and leg (a) returns 200, so
    /// this fact goes RED. That is precisely the point: it is the fact the earlier pins could not
    /// express. Secondary: make the gate decide on the SCOPE role instead of the primary role and
    /// leg (a) goes 200 as well.</para>
    ///
    /// <para><b>S144 red conditions.</b> Let HANDLED_MANUALLY on an EXPORTED_MONTH row through for a
    /// non-GlobalAdmin → leg (a2) returns 200 instead of 403. Stamp <c>{}</c> instead of the derived
    /// set on the DISMISSED → the <c>'{QUAL-149}'</c> row assertion and the event <c>blockedBy</c>
    /// assertion in leg (b) trip.</para>
    /// </summary>
    [Fact]
    public async Task Resolve_ExportedMonth_AsRecalculated_MixedRoleHrWithGlobalAdminScope_Is403_ButMayStillDismiss()
    {
        var url = $"/api/hr/backdate-worklist/{_openRowId}/resolve";
        var mixed = Client(MixedRoleHrWithGlobalAdminScopeToken(EmployeeOrg, "wl_ep_hr_globalscope"));

        // (a) RECALCULATED — refused, because the PRIMARY role claim is LocalHR.
        var recalc = await SendResolveAsync(mixed, url, "\"1\"",
            new { resolution = "RECALCULATED", reason = "Claiming a recalculation I cannot actually run" });
        Assert.Equal(HttpStatusCode.Forbidden, recalc.StatusCode);
        using (var doc = JsonDocument.Parse(await recalc.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetString());
            var reason = doc.RootElement.GetProperty("reason").GetString() ?? string.Empty;
            // The refusal is the OQ-7 GATE, not the org-scope check — the GLOBAL scope cleared that.
            Assert.NotEqual("Actor scope does not cover target organization", reason);
            Assert.Contains("GlobalAdmin", reason, StringComparison.Ordinal);
            Assert.Contains("RECALCULATED", reason, StringComparison.Ordinal);
        }

        // The refusal was a refusal: no state change, no event, no audit row.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND version = 1", _openRowId));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0", Employee));

        // (a2) S144 / TASK-14401 (3b): HANDLED_MANUALLY is gated like RECALCULATED on an
        // EXPORTED_MONTH row — the same mixed-role token is refused, and the row is untouched.
        var handled = await SendResolveAsync(mixed, url, "\"1\"",
            new { resolution = "HANDLED_MANUALLY", reason = "Claiming a manual handling I may not record" });
        Assert.Equal(HttpStatusCode.Forbidden, handled.StatusCode);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND resolution_blocked_by IS NULL AND version = 1", _openRowId));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));

        // (b) The SAME token DISMISSES the SAME row successfully — so leg (a)'s 403 is the gate
        // biting on the VERB, not a token that could never touch this row at all.
        var dismiss = await SendResolveAsync(mixed, url, "\"1\"",
            new { resolution = "DISMISSED", reason = "Handed to a Global Admin to re-plan" });
        Assert.Equal(HttpStatusCode.OK, dismiss.StatusCode);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution = 'DISMISSED' AND resolved_by = 'wl_ep_hr_globalscope' AND version = 2", _openRowId));
        // S144: the DISMISSED is stamped with the derived block set (the row is blocked by QUAL-149).
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution_blocked_by = '{QUAL-149}'", _openRowId));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved' AND event_payload -> 'blockedBy' = '[\"QUAL-149\"]'::jsonb", $"employee-{Employee}"));
    }

    /// <summary>
    /// The two paths OQ-7 (a) deliberately leaves OPEN to HR, so the fix is a targeted refusal and
    /// not a blanket one: (a) DISMISSED on an EXPORTED_MONTH row — dismissing records a judgement,
    /// not a payroll act; (b) RECALCULATED on a SETTLED_YEAR row — its remedy is the settlement
    /// REVERSAL, which is <c>HROrAbove</c> (<c>SettlementReversalEndpoints</c>), so an HR user who
    /// performed that reversal may record it.
    ///
    /// <para><b>Red conditions.</b> (1) Make the gate ignore the resolution verb (refuse HR on any
    /// EXPORTED_MONTH resolve) → leg (a) 403s and fails. (2) Make it ignore <c>row.Kind</c> (refuse
    /// HR on any RECALCULATED) → leg (b) 403s and fails.</para>
    /// </summary>
    [Fact]
    public async Task Resolve_Hr_MayDismissExportedMonth_AndMayRecalculateSettledYear()
    {
        var hr = Client(HrToken(EmployeeOrg, ScopedHrActor));

        // (a) DISMISSED on the EXPORTED_MONTH row — permitted for HR.
        var dismissed = await SendResolveAsync(hr, $"/api/hr/backdate-worklist/{_openRowId}/resolve", "\"1\"",
            new { resolution = "DISMISSED", reason = "March already re-planned outside the tool" });
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution = 'DISMISSED' AND resolved_by = @p1", _openRowId, ScopedHrActor));

        // (b) RECALCULATED on a SETTLED_YEAR row — permitted for HR (the reversal is HROrAbove).
        var settledRowId = await SeedSettledYearRowAsync();
        var recalculated = await SendResolveAsync(hr, $"/api/hr/backdate-worklist/{settledRowId}/resolve", "\"1\"",
            new { resolution = "RECALCULATED", reason = "Reversed and re-settled ferieår 2025" });
        Assert.Equal(HttpStatusCode.OK, recalculated.StatusCode);
        using (var doc = JsonDocument.Parse(await recalculated.Content.ReadAsStringAsync()))
            Assert.Equal("RECALCULATED", doc.RootElement.GetProperty("resolution").GetString());
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND kind = 'SETTLED_YEAR' AND resolution = 'RECALCULATED' AND resolved_by = @p1", settledRowId, ScopedHrActor));
    }

    /// <summary>
    /// Seeds ONE open SETTLED_YEAR row for <see cref="Employee"/> via the skip entry point, which
    /// raises a row even with no active settlement (a degraded, honest baseline — see
    /// <c>HrBackdateWorklistRepositoryTests.WriteForSkippedSettledYears_TupleWithNoActiveSettlement_DegradesToAnUnknownBaseline</c>).
    /// Seeded inside the test rather than in <c>InitializeAsync</c> so the GET pins keep asserting a
    /// single row.
    /// </summary>
    private async Task<Guid> SeedSettledYearRowAsync()
    {
        var repo = _factory.Services.GetRequiredService<HrBackdateWorklistRepository>();
        var dbFactory = _factory.Services.GetRequiredService<DbConnectionFactory>();
        await using var conn = dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var ids = await repo.WriteForSkippedSettledYearsAsync(conn, tx, Employee,
            new WorklistTrigger(WorklistTriggerKinds.ProfileChange, Guid.NewGuid(), new DateOnly(2025, 3, 15), "hr_seed"),
            new[] { ("VACATION", 2025) }, CancellationToken.None);
        await tx.CommitAsync();
        return Assert.Single(ids);
    }

    // ─── HTTP helpers ────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> SendResolveAsync(HttpClient client, string url, string ifMatch, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(ifMatch));
        return await client.SendAsync(req);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private static string GlobalAdminToken() => NewTokenService().GenerateToken(
        employeeId: "wl_ep_admin", name: "S138 QA Admin", role: StatsTidRoles.GlobalAdmin, agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    private static string HrToken(string orgId, string actorId) => NewTokenService().GenerateToken(
        employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR, agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });

    /// <summary>S140 sprint-end review — the MIXED-ROLE claim shape: primary <c>role</c> claim
    /// LocalHR, but a GLOBAL scope stamped <c>Role = GlobalAdmin</c>. <c>GlobalAdminOnly</c> (and
    /// so <c>/api/payroll/recalculate</c>) decides on the PRIMARY role claim alone and refuses this
    /// token, so the in-handler gate must refuse it too. A gate that consulted the scopes array for
    /// its role decision would wrongly admit it — the over-grant this shape pins.</summary>
    private static string MixedRoleHrWithGlobalAdminScopeToken(string orgId, string actorId) =>
        NewTokenService().GenerateToken(
            employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR, agreementCode: "AC", orgId: orgId,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    /// <summary>S140 / TASK-14010 — ABOVE HR but NOT a Global Admin, which is what the OQ-7 (a)
    /// gate must still refuse (a role-floor implementation would wrongly admit this token).</summary>
    private static string LocalAdminToken(string orgId, string actorId) => NewTokenService().GenerateToken(
        employeeId: actorId, name: actorId, role: StatsTidRoles.LocalAdmin, agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, orgId, "ORG_ONLY") });

    private static string EmployeeToken(string employeeId, string orgId) => NewTokenService().GenerateToken(
        employeeId: employeeId, name: employeeId, role: StatsTidRoles.Employee, agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });

    // ─── DB helpers ──────────────────────────────────────────────────────────

    private async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }
}
