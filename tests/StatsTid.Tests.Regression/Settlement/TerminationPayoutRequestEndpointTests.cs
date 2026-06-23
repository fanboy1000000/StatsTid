using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7102 (ADR-033 slice 3b; SPRINT-71 R2/R6/R12 + owner D-B) — the §26 anmodning
/// endpoint, <c>POST /api/admin/employees/{employeeId}/termination-payout-request</c>:
///
/// <list type="bullet">
///   <item><description><b>Happy path:</b> OPEN request row + <c>TerminationPayoutRequested</c>
///   (snapshot-COPIED CrystallizedDays/SettlementBoundaryDate — never recomputed) + ADR-026
///   audit row, ONE tx; the terminated TARGET is the normal case (proves the
///   terminated-inclusive reachability);</description></item>
///   <item><description><b>R6 single-live-request:</b> duplicate → 409;</description></item>
///   <item><description><b>Wrong-row 422 matrix:</b> non-TERMINATION / non-SETTLED /
///   zero-crystallized / sequence-mismatch (DECLARED 422 with the actual sequence) /
///   end-date-not-passed (live in-lock user state, set and null variants);</description></item>
///   <item><description><b>Access/precondition matrix:</b> 404 (no row), 412 (stale If-Match),
///   428 (missing), 422 (body shape), the R9e-style denial rows (non-HR, out-of-subtree)
///   ;</description></item>
///   <item><description><b>R12 race (parked-lock choreography):</b> a reversal committing while
///   the request parks on the employee advisory lock is SEEN by the in-lock re-read → 404,
///   nothing written.</description></item>
/// </list>
///
/// <para>Fixed clock 2026-03-05 (PAT-008 derived host); end date 2026-02-28 ⇒ R6 ferieår 2025,
/// crystallized 12.5 (the SettlementReversalTests anchor set). Seeded VACATION config: quota 25,
/// reset_month 9, carryover_max 5.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TerminationPayoutRequestEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string DisjointOrg = "STY05";
    private const string CoveringOrg = "STY01";  // S93 flat role-scope: covers STY01 by exact ORG_ONLY match (a MAO no longer covers a child)
    private const string VacationType = "VACATION";
    private const string Termination = "TERMINATION";
    private const string HrActorId = "hr_s71_req";

    private static readonly DateOnly Clock = new(2026, 3, 5);
    private static readonly DateOnly EndDate = new(2026, 2, 28);
    private const int EndDateFerieaar = 2025;
    private static readonly DateOnly RequestDate = new(2026, 3, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _app = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // ONE fixed-clock derived host (no go-live ⇒ the poller stays Step-B-dormant; Step A
        // no-ops on the pre-flipped leavers these tests seed) — HTTP + settle drives.
        _app = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Clock))));
        _ = _app.CreateClient(); // boot seeders (org tree + configs)
    }

    public async Task DisposeAsync()
    {
        _app?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Happy path — row + event (snapshot-copied quantities) + audit, one tx; the
    // terminated-target reachability proof (the leaver is DEACTIVATED).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HappyPath_TerminatedTarget_201_Row_Event_Audit()
    {
        var employeeId = await SeedSettledTerminationAsync();

        var rsp = await PostRequestAsync(HrClient(CoveringOrg), employeeId,
            year: EndDateFerieaar, sequence: 1, ifMatch: "\"1\"",
            evidenceNote: "anmodning modtaget pr. mail");

        Assert.Equal((HttpStatusCode)201, rsp.StatusCode);
        Assert.Equal("\"1\"", rsp.Headers.ETag!.Tag);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("requestId").GetInt64() > 0);
        Assert.Equal("OPEN", body.GetProperty("state").GetString());
        Assert.Equal(1, body.GetProperty("settlementSequence").GetInt32());
        Assert.Equal(12.5m, body.GetProperty("crystallizedDays").GetDecimal());
        Assert.Equal(EndDate.ToString("yyyy-MM-dd"), body.GetProperty("settlementBoundaryDate").GetString());
        Assert.Equal(1L, body.GetProperty("version").GetInt64());

        // The durable request row (the 7100 column contract; recorded_by = the JWT actor).
        var row = await ReadRequestRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.NotNull(row);
        Assert.Equal("OPEN", row!.Value.State);
        Assert.Equal(HrActorId, row.Value.RecordedBy);
        Assert.Equal(RequestDate, row.Value.RequestDate);
        Assert.Equal("anmodning modtaget pr. mail", row.Value.EvidenceNote);

        // The 7101 event — quantities COPIED from the settlement snapshot, never recomputed.
        var payload = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "TerminationPayoutRequested");
        Assert.NotNull(payload);
        using (var doc = JsonDocument.Parse(payload!))
        {
            var root = doc.RootElement;
            Assert.Equal(employeeId, root.GetProperty("employeeId").GetString());
            Assert.Equal(VacationType, root.GetProperty("entitlementType").GetString());
            Assert.Equal(EndDateFerieaar, root.GetProperty("entitlementYear").GetInt32());
            Assert.Equal(1, root.GetProperty("settlementSequence").GetInt32());
            Assert.Equal(12.5m, root.GetProperty("crystallizedDays").GetDecimal());
            Assert.Equal(EndDate.ToString("yyyy-MM-dd"), root.GetProperty("settlementBoundaryDate").GetString());
            Assert.Equal(RequestDate.ToString("yyyy-MM-dd"), root.GetProperty("requestDate").GetString());
            Assert.Equal("anmodning modtaget pr. mail", root.GetProperty("evidenceNote").GetString());
            Assert.Equal(HrActorId, root.GetProperty("actorId").GetString());
        }

        // ADR-026 audit-projection row, same tx (TENANT_TARGETED on the leaver's org).
        Assert.Equal(1L, await CountAsync(
            "audit_projection",
            "event_type = 'TerminationPayoutRequested' AND target_resource_id = @r AND target_org_id = @o",
            ("r", employeeId), ("o", OrgId)));

        // The settlement row itself is UNTOUCHED (the request is its own aggregate).
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements",
            "employee_id = @e AND entitlement_year = @y AND settlement_state = 'SETTLED' AND version = 1",
            ("e", employeeId), ("y", EndDateFerieaar)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R6 — one non-voided request per settlement row: duplicate → 409.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DuplicateRequest_409_OnlyOneRowPersisted()
    {
        var employeeId = await SeedSettledTerminationAsync();
        var first = await PostRequestAsync(HrClient(CoveringOrg), employeeId, EndDateFerieaar, 1, "\"1\"");
        Assert.Equal((HttpStatusCode)201, first.StatusCode);

        var second = await PostRequestAsync(HrClient(CoveringOrg), employeeId, EndDateFerieaar, 1, "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("already exists", body.GetProperty("error").GetString());
        Assert.Equal(1L, await CountAsync(
            "termination_payout_requests", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "TerminationPayoutRequested"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Wrong-row 422 matrix (R6 guards; DECLARED: sequence-mismatch = 422 on this surface).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WrongRow_YearEndTrigger_422()
    {
        var employeeId = await SeedEmployeeAsync(); // ACTIVE — the trigger guard fires first
        await SeedSettlementRowAsync(employeeId, 2024, trigger: "YEAR_END", state: "SETTLED",
            crystallizedDays: null);

        var rsp = await PostRequestAsync(HrClient(CoveringOrg), employeeId, 2024, 1, "\"1\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("TERMINATION", body.GetProperty("error").GetString());
        Assert.Equal(0L, await CountAsync("termination_payout_requests", "employee_id = @e", ("e", employeeId)));
    }

    [Fact]
    public async Task WrongRow_PendingReviewTermination_422()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, EndDateFerieaar, trigger: Termination,
            state: "PENDING_REVIEW", crystallizedDays: 0m, forfeitDays: 2.5m);

        var rsp = await PostRequestAsync(HrClient(CoveringOrg), employeeId, EndDateFerieaar, 1, "\"1\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not SETTLED", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WrongRow_ZeroCrystallized_422()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SeedSettlementRowAsync(employeeId, EndDateFerieaar, trigger: Termination,
            state: "SETTLED", crystallizedDays: 0m);

        var rsp = await PostRequestAsync(HrClient(CoveringOrg), employeeId, EndDateFerieaar, 1, "\"1\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("CrystallizedDays", body.GetProperty("error").GetString());
    }

    /// <summary>R2/B1 — the generation binding refuses on the SEQUENCE before any version
    /// comparison; 422 (the wrong-row bucket, DECLARED) with the actual sequence.</summary>
    [Fact]
    public async Task WrongRow_SequenceMismatch_422_CarriesActualSequence()
    {
        var employeeId = await SeedSettledTerminationAsync();

        var rsp = await PostRequestAsync(HrClient(CoveringOrg), employeeId, EndDateFerieaar,
            sequence: 99, ifMatch: "\"1\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(99, body.GetProperty("expectedSettlementSequence").GetInt32());
        Assert.Equal(1, body.GetProperty("actualSettlementSequence").GetInt32());
        Assert.Equal(0L, await CountAsync("termination_payout_requests", "employee_id = @e", ("e", employeeId)));
    }

    /// <summary>The end-date-passed guard reads the CURRENT in-lock user state (the B1 lesson):
    /// a future-dated and a null end date both 422 even against a SETTLED TERMINATION row.</summary>
    [Fact]
    public async Task EndDateNotPassed_FutureAndNullVariants_422()
    {
        // Future-dated variant.
        var future = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(future, EndDateFerieaar, trigger: Termination,
            state: "SETTLED", crystallizedDays: 12.5m);
        await ExecAsync("UPDATE users SET employment_end_date = @d WHERE user_id = @id",
            ("id", future), ("d", new DateOnly(2026, 12, 31))); // >= fixed clock 2026-03-05
        var futureRsp = await PostRequestAsync(HrClient(CoveringOrg), future, EndDateFerieaar, 1, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, futureRsp.StatusCode);
        var futureBody = await futureRsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("not passed", futureBody.GetProperty("error").GetString());

        // Null variant (no leaver fact at all).
        var noDate = await SeedEmployeeAsync();
        await SeedSettlementRowAsync(noDate, EndDateFerieaar, trigger: Termination,
            state: "SETTLED", crystallizedDays: 12.5m);
        var nullRsp = await PostRequestAsync(HrClient(CoveringOrg), noDate, EndDateFerieaar, 1, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, nullRsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Access / precondition matrix — 404 / 412 / 428 / body-shape 422.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PreconditionMatrix_404_412_428_BodyShape422()
    {
        var employeeId = await SeedSettledTerminationAsync();
        var client = HrClient(CoveringOrg);

        // 404 — no active settlement for the year.
        var noRow = await PostRequestAsync(client, employeeId, year: 2020, sequence: 1, ifMatch: "\"1\"");
        Assert.Equal(HttpStatusCode.NotFound, noRow.StatusCode);

        // 412 — stale settlement If-Match, with the actual version.
        var stale = await PostRequestAsync(client, employeeId, EndDateFerieaar, 1, "\"99\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var staleBody = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1L, staleBody.GetProperty("actualVersion").GetInt64());

        // 428 — missing If-Match (admin-strict).
        var missing = await PostRequestAsync(client, employeeId, EndDateFerieaar, 1, ifMatch: null);
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);

        // 422 — body shape: missing requestDate / missing sequence / missing year.
        var noDate = await SendAsync(client, employeeId, new { entitlementYear = EndDateFerieaar, expectedSettlementSequence = 1 }, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noDate.StatusCode);
        var noSeq = await SendAsync(client, employeeId, new { entitlementYear = EndDateFerieaar, requestDate = RequestDate }, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noSeq.StatusCode);
        var noYear = await SendAsync(client, employeeId, new { expectedSettlementSequence = 1, requestDate = RequestDate }, "\"1\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noYear.StatusCode);

        // Nothing leaked from the refused attempts.
        Assert.Equal(0L, await CountAsync("termination_payout_requests", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationPayoutRequested"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // Denial matrix (the R9e family, extended to this 3b surface): non-HR 403,
    // out-of-subtree HR 403. (Terminated-target reachability = the happy path above;
    // DECLARED: no self-target exclusion on this endpoint — R4's exclusion is
    // end-date-mutation-specific and R6/D-B pin none here.)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DenialMatrix_NonHr_And_OutOfSubtree_403()
    {
        var employeeId = await SeedSettledTerminationAsync();

        var emp = await PostRequestAsync(ClientWith(EmployeeToken("emp_s71_req", OrgId)),
            employeeId, EndDateFerieaar, 1, "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, emp.StatusCode);

        var leader = await PostRequestAsync(ClientWith(LeaderToken("ldr_s71_req", OrgId)),
            employeeId, EndDateFerieaar, 1, "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, leader.StatusCode);

        var disjoint = await PostRequestAsync(HrClient(DisjointOrg), employeeId, EndDateFerieaar, 1, "\"1\"");
        Assert.Equal(HttpStatusCode.Forbidden, disjoint.StatusCode);

        Assert.Equal(0L, await CountAsync("termination_payout_requests", "employee_id = @e", ("e", employeeId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 — request-vs-reversal race (the parked-lock choreography): the POST parks on the
    // employee advisory lock; a reversal commits in the window; the resumed handler re-reads
    // IN-LOCK, finds no active row → 404, NOTHING written.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R12_RequestParksOnLock_ReversalCommitsInWindow_404_NothingWritten()
    {
        var employeeId = await SeedSettledTerminationAsync();
        var client = HrClient(CoveringOrg);

        await using var lockConn = new NpgsqlConnection(_harness.ConnectionString);
        await lockConn.OpenAsync();
        await using var lockTx = await lockConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", lockConn, lockTx))
        {
            lockCmd.Parameters.AddWithValue("employeeId", employeeId);
            await lockCmd.ExecuteScalarAsync();
        }

        var postTask = PostRequestAsync(client, employeeId, EndDateFerieaar, 1, "\"1\"");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var parked = false;
        while (!parked && DateTime.UtcNow < deadline)
        {
            if (postTask.IsCompleted)
            {
                var early = await postTask;
                Assert.Fail($"the POST completed ({(int)early.StatusCode}) without parking on the held " +
                            "employee advisory lock — the endpoint is not acquiring the R12 lock first.");
            }
            parked = await IsAdvisoryLockWaiterPresentAsync();
            if (!parked) await Task.Delay(100);
        }
        Assert.True(parked, "the POST never parked on the held employee advisory lock within 30s.");

        // The committed outcome of a winning bare reversal (the winner would have held the lock).
        await ExecAsync(
            """
            UPDATE vacation_settlements
               SET settlement_state = 'REVERSED', bare_reversal_not_due = TRUE,
                   version = version + 1, updated_at = NOW()
             WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y AND sequence = 1
            """, ("e", employeeId), ("t", VacationType), ("y", EndDateFerieaar));

        await lockTx.RollbackAsync(); // release — the handler resumes and re-reads in-lock

        var rsp = await postTask;
        Assert.Equal(HttpStatusCode.NotFound, rsp.StatusCode);
        Assert.Equal(0L, await CountAsync("termination_payout_requests", "employee_id = @e", ("e", employeeId)));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "TerminationPayoutRequested"));
    }

    // ─────────────────────────────── HTTP helpers ───────────────────────────────

    private static string RequestUrl(string employeeId) =>
        $"/api/admin/employees/{employeeId}/termination-payout-request";

    private async Task<HttpResponseMessage> PostRequestAsync(
        HttpClient client, string employeeId, int year, int sequence, string? ifMatch,
        string? evidenceNote = null)
        => await SendAsync(client, employeeId, new
        {
            entitlementYear = year,
            expectedSettlementSequence = sequence,
            requestDate = RequestDate,
            evidenceNote,
        }, ifMatch);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string employeeId, object body, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, RequestUrl(employeeId))
        {
            Content = JsonContent.Create(body),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    // ─────────────────────────────── clients / tokens ───────────────────────────────

    private HttpClient ClientWith(string bearer)
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return client;
    }

    private HttpClient HrClient(string scopeOrgId) => ClientWith(HrToken(HrActorId, scopeOrgId));

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

    // ─────────────────────────────── seeding / drives ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s71_req_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>The standard subject: a DEACTIVATED leaver with a REAL settled TERMINATION row
    /// (sequence 1, version 1; snapshot crystallized 12.5, boundary = the end date) produced by
    /// the actual settlement pass — the snapshot is the production shape, not a hand-rolled one.</summary>
    private async Task<string> SeedSettledTerminationAsync()
    {
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        var settle = _app.Services.GetRequiredService<VacationSettlementService>();
        await using var conn = _app.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var outcome = await settle.SettleAsync(
            employeeId, VacationType, EndDateFerieaar, Termination, conn, tx, leaverGoLiveFloor: null);
        await tx.CommitAsync();
        Assert.True(outcome.DidSettle, "the TERMINATION settle drive must produce the settled row");
        Assert.Equal("SETTLED", outcome.Row!.SettlementState);
        return employeeId;
    }

    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate)
    {
        await ExecAsync(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, ("id", employeeId), ("endDate", endDate));
    }

    /// <summary>Direct wrong-row seed (camelCase snapshot keys — the Web deserializer shape the
    /// endpoint reads). <paramref name="crystallizedDays"/> null omits the field entirely.</summary>
    private async Task SeedSettlementRowAsync(
        string employeeId, int year, string trigger, string state,
        decimal? crystallizedDays, decimal forfeitDays = 0m)
    {
        var snapshot = new Dictionary<string, object?>
        {
            ["recordedAbsences"] = Array.Empty<object>(),
            ["earned"] = 25m,
            ["used"] = 0m,
            ["planned"] = 0m,
            ["carryoverIn"] = 0m,
            ["annualQuota"] = 25m,
            ["carryoverMax"] = 5m,
            ["resetMonth"] = 9,
            ["okVersion"] = "OK24",
            ["transferAgreementDays"] = 0m,
            ["isFeriehindret"] = false,
            ["settlementBoundaryDate"] = EndDate.ToString("yyyy-MM-dd"),
        };
        if (crystallizedDays is not null)
        {
            snapshot["crystallizedDays"] = crystallizedDays.Value;
            snapshot["terminationDate"] = EndDate.ToString("yyyy-MM-dd");
            snapshot["crystallizationBasis"] = "S26_WHOLE_MONTH";
        }
        await ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days, version)
            VALUES (@e, @t, @y, 1, @state, @trigger, @snapshot::jsonb, 0, 0, @forfeit, 1)
            """,
            ("e", employeeId), ("t", VacationType), ("y", year), ("state", state),
            ("trigger", trigger), ("snapshot", JsonSerializer.Serialize(snapshot)),
            ("forfeit", forfeitDays));
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private async Task<(string State, string RecordedBy, DateOnly RequestDate, string? EvidenceNote)?>
        ReadRequestRowAsync(string employeeId, int year, int settlementSequence)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT state, recorded_by, request_date, evidence_note
            FROM termination_payout_requests
            WHERE employee_id = @e AND entitlement_type = @t
              AND entitlement_year = @y AND settlement_sequence = @s
            ORDER BY request_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("s", settlementSequence);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1),
            reader.GetFieldValue<DateOnly>(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
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

    private async Task<long> CountOutboxByTypeAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = @t", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<bool> IsAdvisoryLockWaiterPresentAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND wait_event_type = 'Lock'
              AND query ILIKE '%pg_advisory_xact_lock%'
            """, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<long> CountAsync(string table, string whereClause, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table} WHERE {whereClause}", conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
