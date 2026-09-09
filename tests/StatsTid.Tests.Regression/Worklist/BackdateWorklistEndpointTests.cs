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

        var trigger = Assert.Single(row.GetProperty("triggers").EnumerateArray());
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

        // (4) open=true (default) no longer lists it; open=false does, with the resolution fields.
        using (var openDoc = JsonDocument.Parse(await client.GetStringAsync($"/api/hr/backdate-worklist?employeeId={Employee}")))
            Assert.Empty(openDoc.RootElement.EnumerateArray());
        using (var allDoc = JsonDocument.Parse(await client.GetStringAsync($"/api/hr/backdate-worklist?employeeId={Employee}&open=false")))
        {
            var row = Assert.Single(allDoc.RootElement.EnumerateArray());
            Assert.Equal("DISMISSED", row.GetProperty("resolution").GetString());
            Assert.Equal(ScopedHrActor, row.GetProperty("resolvedBy").GetString());
            Assert.Equal(2L, row.GetProperty("version").GetInt64());
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
    public async Task Resolve_ExportedMonth_AsRecalculated_Hr403_LocalAdmin403_GlobalAdmin200_ForeignHrStillScope403()
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

        // (4) The SAME request, differing ONLY in the actor's role, succeeds for a Global Admin —
        // so the 403s above are the rule biting, not the request being malformed, and the gate does
        // not over-block the one role that CAN perform the remedy.
        var admin = await SendResolveAsync(Client(GlobalAdminToken()), url, "\"1\"", recalculated);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("\"2\"", admin.Headers.ETag!.Tag);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution = 'RECALCULATED' AND resolved_by = 'wl_ep_admin' AND version = 2", _openRowId));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));
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

        // (b) The SAME token DISMISSES the SAME row successfully — so leg (a)'s 403 is the gate
        // biting on the VERB, not a token that could never touch this row at all.
        var dismiss = await SendResolveAsync(mixed, url, "\"1\"",
            new { resolution = "DISMISSED", reason = "Handed to a Global Admin to re-plan" });
        Assert.Equal(HttpStatusCode.OK, dismiss.StatusCode);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution = 'DISMISSED' AND resolved_by = 'wl_ep_hr_globalscope' AND version = 2", _openRowId));
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
