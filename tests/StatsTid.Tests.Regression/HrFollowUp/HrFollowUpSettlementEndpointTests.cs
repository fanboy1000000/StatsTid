using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.HrFollowUp;

/// <summary>
/// S140 / TASK-14005 (refinement B3) — Docker-gated pins for the SETTLEMENT-FAMILY HR follow-up
/// reads (<see cref="HrFollowUpSettlementEndpoints"/>, TASK-14003; HRP-005, HRP-005b, HRP-007,
/// HRP-010) plus the pre-existing payout-pending endpoint's seeded count (refinement B3's
/// "payout-pending (1, seeded)" item).
///
/// <para>
/// <b>RED-FIRST, reasoned from the spec.</b> Docker is unavailable on the authoring machine, so
/// every pin below is derived from <c>HrFollowUpSettlementEndpoints.cs</c> /
/// <c>HrFollowUpSettlementReadRepository.cs</c> / <c>VacationSettlementService.cs</c> as read, not
/// from an observed run — these first execute (and are CI-verified, never claimed green locally)
/// in the sprint-close watched CI run. Each fact's doc comment states the RED condition.
/// </para>
///
/// <para>
/// <b>PAT-008 — one fixed anchor for the whole class, one WAF host per fact.</b>
/// <c>F = 2025-11-12</c> (Wednesday; OK24 side of the 2026-04-01 cutover; also the anchor the
/// refinement names for the §21 November-reminder window). Every fact derives its OWN host via
/// <c>_factory.WithFixedToday(F)</c> and boots it exactly once — never the base <c>_factory</c>
/// alongside it, which would start a second copy of every hosted service (the delegation-expiry
/// sweep among them) against the same container.
/// </para>
///
/// <para>
/// <b>Age fields floor at zero (Hard Rule 5).</b> Where age matters, <c>created_at</c> is stamped
/// EXPLICITLY at UTC midnight of an F-relative date, never left at the row default (which would
/// bind to the real wall clock — under F sitting in 2025, that collapses to age 0 against the
/// fixed anchor and proves nothing).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class HrFollowUpSettlementEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string VacationType = "VACATION";

    private const string OrgA = "STY_HRFU_SETA";
    private const string OrgForeign = "STY_HRFU_SETF";

    /// <summary>Wednesday, OK24 side (S140's own November §21 anchor) — the one fixed "today" for
    /// every fact in this class.</summary>
    private static readonly DateOnly F = new(2025, 11, 12);

    /// <summary>The ferieår whose §21 stk.2 deadline is 31 Dec 2025 under the AC/OK24 reset-month-9
    /// geometry: E + 1 = 2025 ⇒ E = 2024 (ferieår 2024 = 1 Sep 2024 .. 31 Aug 2025 — CLOSED by F).
    /// Derived the same way <c>HrFollowUpSettlementEndpoints.ResolveSection21TargetYear</c> derives
    /// it; asserted directly in <see cref="TransferAgreementsNeeded_NovemberAnchor_Appears_DropsOnceRecorded_RecordMatchesWrite"/>.</summary>
    private const int TargetYear = 2024;

    private static readonly DateOnly Section21Deadline = new(2025, 12, 31);
    private static readonly DateOnly Section21AccrualEnd = new(2025, 8, 31); // ferieår 2024's END

    /// <summary>A hire date INSIDE ferieår 2024 (1 Sep 2024 – 31 Aug 2025): the §21 question APPLIES
    /// — the employee can hold part of that ferieår — but dated history anchored here does not cover
    /// the ferieår START, so the valuation fails closed and that IS a real signal (TASK-14012).</summary>
    private static readonly DateOnly HiredDuringTargetFerieaar = new(2025, 1, 1);

    /// <summary>A hire date AFTER ferieår 2024 finished accruing (<see cref="Section21AccrualEnd"/>)
    /// but before the anchor F: the employee cannot hold a single day of that ferieår, so the §21
    /// question is NOT APPLICABLE and they belong in neither list (TASK-14012).</summary>
    private static readonly DateOnly HiredAfterTargetFerieaar = new(2025, 9, 15);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Deliberately NOT booted here (PAT-008 "one host per fact") — each fact derives its own
        // fixed-clock host below.
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static int Seq = 0;
    private static string NextId(string prefix) => $"{prefix}_{Interlocked.Increment(ref Seq)}";

    // ════════════════════════════════════════════════════════════════════════
    // Org scope — empty accessible set ⇒ 403 (never an empty 200), across all three list reads.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// RED if any of the three list handlers were changed to return an empty 200 instead of
    /// refusing, or if the empty-scope branch (<c>accessibleOrgIds is {'{'} Count: 0 {'}'}</c>) were
    /// deleted (the actor would then fall through to a null-scope NRE or, worse, an unrestricted
    /// read). The actor passes <c>HROrAbove</c> (role = LocalHR clears the POLICY's role check) but
    /// its one scope carries <c>ScopeType = "ORG_AND_DESCENDANTS"</c> — the S93-dropped legacy type
    /// <c>OrgScopeValidator.GetAccessibleOrgsAsync</c> explicitly discards, so the accessible-org set
    /// is empty even though a scope exists (a stale-token shape, not "no scopes at all").
    /// </summary>
    [Fact]
    public async Task OrgScope_ActorWithNoContributingScope_403_AcrossAllThreeListReads()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();
        var noScope = Client(host, NoContributingScopeHrToken());

        foreach (var url in new[]
                 {
                     "/api/hr/follow-up/settlement-reviews",
                     "/api/hr/follow-up/termination-payouts-unrequested",
                     "/api/hr/follow-up/transfer-agreements-needed",
                 })
        {
            var rsp = await noScope.GetAsync(url);
            Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
            using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync());
            Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetString());
            Assert.Equal("No HR-level organisation scope", doc.RootElement.GetProperty("reason").GetString());
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // HRP-005 + HRP-005b — GET /api/hr/follow-up/settlement-reviews
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// HRP-005 pin (1): a PENDING_REVIEW row appears with <c>source = "row"</c>. RED if the SQL's
    /// <c>settlement_state = 'PENDING_REVIEW'</c> term were dropped or the source literal flipped.
    /// Also the org-scope checks for this read: own-org HR sees it, foreign-org HR does not (a plain
    /// "own org / not another org" check — this read has no stamped-org column to diverge from,
    /// every branch already joins on <c>u.primary_org_id</c>, so no divergence seed is needed here);
    /// and a GlobalAdmin sees it too — the <c>null</c> accessible-orgs sentinel bypasses the org
    /// filter entirely. RED if the GlobalAdmin's GLOBAL scope stopped short-circuiting
    /// <c>OrgScopeValidator.GetAccessibleOrgsAsync</c> to <c>null</c>.
    /// </summary>
    [Fact]
    public async Task SettlementReviews_RowSource_PendingReview_Appears_ScopedToOwnOrg()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("set_pr");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedSettlementRowAsync(
            employeeId, TargetYear, sequence: 1, state: "PENDING_REVIEW", trigger: "YEAR_END",
            crystallizedDays: null, forfeitDays: 3.5m, createdAt: AtUtcMidnight(F.AddDays(-4)));

        var own = Client(host, HrToken(OrgA));
        var rsp = await own.GetAsync("/api/hr/follow-up/settlement-reviews");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
        Assert.Equal("row", item.GetProperty("source").GetString());
        Assert.Equal("PENDING_REVIEW", item.GetProperty("settlementState").GetString());
        Assert.Equal(TargetYear, item.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, item.GetProperty("settlementSequence").GetInt32());
        Assert.Equal(3.5m, item.GetProperty("flaggedDays").GetDecimal());
        Assert.Equal(1L, item.GetProperty("version").GetInt64());
        Assert.Equal(4, item.GetProperty("ageDays").GetInt32()); // F − (F−4) = 4, created_at pinned explicitly

        var foreign = Client(host, HrToken(OrgForeign));
        using var foreignDoc = JsonDocument.Parse(await foreign.GetStringAsync("/api/hr/follow-up/settlement-reviews"));
        Assert.DoesNotContain(foreignDoc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);

        var admin = Client(host, GlobalAdminToken());
        using var adminDoc = JsonDocument.Parse(await admin.GetStringAsync("/api/hr/follow-up/settlement-reviews"));
        Assert.Contains(adminDoc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    /// <summary>
    /// HRP-005b pins (2 + 3 + 4), the event-sourced half — seeded through the REAL emit path
    /// (<c>VacationSettlementService.SettleAsync</c>, the publisher left RUNNING) per the task's hard
    /// rule, never by a direct INSERT into <c>events</c>.
    ///
    /// <para>Sequence: (a) a clean YEAR_END SETTLED row wins ferieår 2023 for an active employee
    /// (used=20, so the payout partition is clean and the ONLY flag emitted below is the refusal's —
    /// the <c>TerminationSettlementTests</c> precedent); (b) the employee becomes a leaver INSIDE
    /// that ferieår (end date 28 Feb 2024, within [1 Sep 2023, 31 Aug 2024]); (c) the TERMINATION
    /// pass is invoked TWICE against the still-active YEAR_END row — each call is a genuine R7b
    /// refusal and emits its OWN <c>SettlementManualReviewFlagged</c> event referencing the SAME
    /// tuple (employee, VACATION, 2023, sequence 1).</para>
    ///
    /// <para><b>RED conditions.</b> Pin 2 (appears, source=event, after drain): RED if the read
    /// repository's event predicate (<c>data-&gt;'snapshot' IS NULL</c> / <c>flaggedDays = 0</c>)
    /// were narrowed to exclude a still-PENDING/active-row conflict flag, or if the test asserted
    /// before the publisher drained (flakes to "item absent"). Pin 3 (flagged twice ⇒ appears once):
    /// RED if the repository's <c>GROUP BY</c> collapse (identity tuple, MIN(occurred_at)) were
    /// removed — two raw events would then surface as two items. Pin 4 (disappears once REVERSED):
    /// RED if the event branch's <c>s.settlement_state &lt;&gt; 'REVERSED'</c> term were dropped.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SettlementReviews_EventSource_RefusedTermination_FlaggedTwiceOnce_DisappearsOnReversal()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient(); // boots the host — the outbox publisher starts HERE

        var employeeId = NextId("set_conflict");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedBalanceAsync(employeeId, 2023, used: 20m, planned: 0m, carryoverIn: 0m);

        var service = host.Services.GetRequiredService<VacationSettlementService>();
        var dbFactory = host.Services.GetRequiredService<DbConnectionFactory>();

        var yearEnd = await SettleAsync(dbFactory, service, employeeId, 2023, "YEAR_END");
        Assert.True(yearEnd.DidSettle);
        Assert.Equal("SETTLED", yearEnd.Row!.SettlementState);

        await MarkLeaverAsync(employeeId, new DateOnly(2024, 2, 28)); // inside ferieår 2023

        var first = await SettleAsync(dbFactory, service, employeeId, 2023, "TERMINATION");
        Assert.True(first.RefusedConflict);
        var second = await SettleAsync(dbFactory, service, employeeId, 2023, "TERMINATION");
        Assert.True(second.RefusedConflict);

        // Two RAW events exist (the mechanism this pin exists to prove is NOT double-counted).
        var rawEvents = await WaitForEventsAsync(employeeId, "SettlementManualReviewFlagged", expectedCount: 2, timeout: TimeSpan.FromSeconds(15));
        Assert.Equal(2, rawEvents.Count);
        var earliestOccurredAt = rawEvents.Min(e => e.OccurredAt);

        var hr = Client(host, HrToken(OrgA));
        using (var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/settlement-reviews")))
        {
            var matches = doc.RootElement.GetProperty("items").EnumerateArray()
                .Where(i => i.GetProperty("employeeId").GetString() == employeeId && i.GetProperty("source").GetString() == "event")
                .ToList();
            var item = Assert.Single(matches); // pin 3: flagged TWICE, surfaced ONCE
            Assert.Equal(2023, item.GetProperty("entitlementYear").GetInt32());
            Assert.Equal(1, item.GetProperty("settlementSequence").GetInt32());
            Assert.Equal(0m, item.GetProperty("flaggedDays").GetDecimal());
            Assert.Equal(earliestOccurredAt, item.GetProperty("ageAnchor").GetDateTimeOffset()); // aged from the FIRST flag
            Assert.NotNull(doc.RootElement.GetProperty("eventSourceLagNote").GetString());
        }

        // Pin 4: reverse the referenced YEAR_END row (the conflict's target) → the event-item exits.
        await ExecAsync(
            "UPDATE vacation_settlements SET settlement_state = 'REVERSED' WHERE employee_id = @p0 AND entitlement_type = @p1 AND entitlement_year = @p2 AND sequence = 1",
            employeeId, VacationType, 2023);
        using (var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/settlement-reviews")))
        {
            Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
                i => i.GetProperty("employeeId").GetString() == employeeId);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // HRP-007 — GET /api/hr/follow-up/termination-payouts-unrequested
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// HRP-007 pins (1 + 2): a SETTLED TERMINATION row with crystallised days &gt; 0 and no live
    /// request appears; it drops once a <c>termination_payout_requests</c> row (state OPEN) is
    /// recorded for the SAME (employee, type, year, sequence). RED if either the
    /// <c>settlement_state = 'SETTLED'</c> / <c>trigger = 'TERMINATION'</c> terms, or the
    /// <c>NOT EXISTS(... state &lt;&gt; 'VOIDED_BY_REVERSAL')</c> anti-join, were dropped.
    /// </summary>
    [Fact]
    public async Task TerminationPayouts_SettledWithCrystallizedDays_Appears_DropsOnceRequestRecorded()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("term_pay");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedTerminationSettlementRowAsync(
            employeeId, TargetYear, sequence: 1, state: "SETTLED", crystallizedDays: 12.5m,
            createdAt: AtUtcMidnight(F.AddDays(-10)));

        var hr = Client(host, HrToken(OrgA));
        using (var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/termination-payouts-unrequested")))
        {
            var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray(),
                i => i.GetProperty("employeeId").GetString() == employeeId);
            Assert.Equal(12.5m, item.GetProperty("crystallizedDays").GetDecimal());
            Assert.Equal(10, item.GetProperty("ageDays").GetInt32());
        }

        await SeedTerminationPayoutRequestAsync(employeeId, TargetYear, sequence: 1, state: "OPEN");

        using (var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/termination-payouts-unrequested")))
        {
            Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
                i => i.GetProperty("employeeId").GetString() == employeeId);
        }
    }

    /// <summary>
    /// HRP-007 pins (3 + 4): the row STAYS ABSENT after a bare reversal (settlement REVERSED, its
    /// request VOIDED_BY_REVERSAL in the same logical transaction — seeded as the resulting state,
    /// since this read is a plain table predicate with no event dependency); the SUCCESSOR row
    /// (sequence 2, SETTLED, crystallised, no request of its own) appears under reverse-and-supersede.
    ///
    /// <para><b>RED conditions.</b> Absence after bare reversal: RED if the read dropped the
    /// <c>settlement_state = 'SETTLED'</c> term and relied on the request anti-join alone — a VOIDED
    /// request satisfies "no LIVE request" on its own, so ONLY the settlement-state term keeps this
    /// row off the tile. Successor appears: RED if the read were changed to look at the settlement's
    /// EMPLOYEE/TYPE/YEAR alone without joining the request on the EXACT sequence, which would let
    /// the reversed row's now-VOIDED request wrongly "cover" the successor.</para>
    /// </summary>
    [Fact]
    public async Task TerminationPayouts_StaysAbsentAfterBareReversal_SuccessorAppearsUnderReverseAndSupersede()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("term_reverse");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedTerminationSettlementRowAsync(employeeId, TargetYear, sequence: 1, state: "REVERSED", crystallizedDays: 12.5m);
        await SeedTerminationPayoutRequestAsync(employeeId, TargetYear, sequence: 1, state: "VOIDED_BY_REVERSAL");
        await SeedTerminationSettlementRowAsync(employeeId, TargetYear, sequence: 2, state: "SETTLED", crystallizedDays: 8.0m);

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/termination-payouts-unrequested"));
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("employeeId").GetString() == employeeId).ToList();
        var item = Assert.Single(items); // the REVERSED sequence-1 row never appears
        Assert.Equal(2, item.GetProperty("settlementSequence").GetInt32());
        Assert.Equal(8.0m, item.GetProperty("crystallizedDays").GetDecimal());
    }

    /// <summary>HRP-007 pin (5): a WAIVED §7 claim never appears, even SETTLED with a positive
    /// crystallised quantity. RED if the <c>review_disposition &lt;&gt; 'WAIVED'</c> term were
    /// dropped.</summary>
    [Fact]
    public async Task TerminationPayouts_WaivedClaim_NeverAppears()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("term_waived");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedTerminationSettlementRowAsync(
            employeeId, TargetYear, sequence: 1, state: "SETTLED", crystallizedDays: 12.5m, reviewDisposition: "WAIVED");

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/termination-payouts-unrequested"));
        Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    // ════════════════════════════════════════════════════════════════════════
    // HRP-010 — GET /api/hr/follow-up/transfer-agreements-needed (+ the record read/write)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// HRP-010 pins (1 + 2 + 8): at the November anchor F, an employee with an untaken ferieår-2024
    /// remainder (UnderCap &gt; 0) and no recorded §21 agreement appears with the correct
    /// days-to-deadline; once the agreement is POSTed the same employee drops off; the record GET
    /// returns exactly what the POST wrote (with the derived deadline).
    ///
    /// <para>Seeded balance: used=20 against a full ferieår earning of 25 (MONTHLY_ACCRUAL, quota 25,
    /// 12 whole months Sep2024..Aug2025) ⇒ disposable = 5, underCap = min(5, carryover_max 5) = 5 &gt; 0.
    /// </para>
    ///
    /// <para><b>RED conditions.</b> Appears/UnderCap: RED if <c>Partition(snapshot).UnderCap &gt; 0</c>
    /// were replaced by any other threshold, or if the candidate population's active-settlement /
    /// leaver exclusions were widened to exclude this employee wrongly. Drops after recording: RED if
    /// the candidates SQL's <c>NOT EXISTS (vacation_transfer_agreements ...)</c> term were dropped.
    /// Record round-trip: RED if the GET read a different column than the POST wrote, or if the
    /// derived <c>deadline</c> used a different resetMonth/geometry than the list read's.</para>
    /// </summary>
    [Fact]
    public async Task TransferAgreementsNeeded_NovemberAnchor_Appears_DropsOnceRecorded_RecordMatchesWrite()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("s21_needs");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedBalanceAsync(employeeId, TargetYear, used: 20m, planned: 0m, carryoverIn: 0m);

        var hr = Client(host, HrToken(OrgA));

        using (var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/transfer-agreements-needed")))
        {
            Assert.True(doc.RootElement.GetProperty("windowOpen").GetBoolean());
            Assert.Equal(TargetYear, doc.RootElement.GetProperty("entitlementYear").GetInt32());
            Assert.Equal(Section21Deadline, ReadDate(doc.RootElement.GetProperty("deadline")));
            Assert.Equal(49, doc.RootElement.GetProperty("daysToDeadline").GetInt32()); // 12 Nov → 31 Dec
            var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray(),
                i => i.GetProperty("employeeId").GetString() == employeeId);
            Assert.Equal(5.0m, item.GetProperty("underCapDays").GetDecimal());
            Assert.Equal(5.0m, item.GetProperty("carryoverMax").GetDecimal());
            Assert.Equal(Section21AccrualEnd, ReadDate(item.GetProperty("ageAnchorDate")));
            Assert.Equal(0, doc.RootElement.GetProperty("cannotComputeCount").GetInt32());
        }

        // Record the §21 agreement via the REAL write endpoint (POST, create-only, no If-Match).
        var postRsp = await hr.PostAsJsonAsync($"/api/vacation-transfer-agreements/{employeeId}", new
        {
            entitlementYear = TargetYear,
            entitlementType = VacationType,
            transferDays = 2.0m,
            agreementDate = F,
        });
        Assert.Equal(HttpStatusCode.Created, postRsp.StatusCode);
        using (var posted = JsonDocument.Parse(await postRsp.Content.ReadAsStringAsync()))
        {
            Assert.Equal(employeeId, posted.RootElement.GetProperty("employeeId").GetString());
            Assert.Equal(2.0m, posted.RootElement.GetProperty("transferDays").GetDecimal());
            Assert.Equal(F, ReadDate(posted.RootElement.GetProperty("agreementDate")));
            Assert.Equal(1L, posted.RootElement.GetProperty("version").GetInt64());
        }

        using (var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/transfer-agreements-needed")))
        {
            Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
                i => i.GetProperty("employeeId").GetString() == employeeId);
        }

        // The record GET returns exactly what the POST wrote.
        using (var doc = JsonDocument.Parse(await hr.GetStringAsync($"/api/vacation-transfer-agreements/{employeeId}")))
        {
            Assert.Equal(employeeId, doc.RootElement.GetProperty("employeeId").GetString());
            var agreement = Assert.Single(doc.RootElement.GetProperty("agreements").EnumerateArray());
            Assert.Equal(TargetYear, agreement.GetProperty("entitlementYear").GetInt32());
            Assert.Equal(VacationType, agreement.GetProperty("entitlementType").GetString());
            Assert.Equal(2.0m, agreement.GetProperty("transferDays").GetDecimal());
            Assert.Equal(F, ReadDate(agreement.GetProperty("agreementDate")));
            Assert.Equal("hr_s140_actor", agreement.GetProperty("recordedBy").GetString());
            Assert.Equal(1L, agreement.GetProperty("version").GetInt64());
            Assert.Equal(Section21Deadline, ReadDate(agreement.GetProperty("deadline")));
        }
    }

    /// <summary>HRP-010 pin (3): an employee with none of the tranche (fully consumed — disposable
    /// 0 ⇒ UnderCap 0) does not appear in EITHER list. RED if a zero UnderCap were treated as
    /// "needed" (e.g. a <c>&gt;=</c> instead of <c>&gt;</c> threshold).</summary>
    [Fact]
    public async Task TransferAgreementsNeeded_UnderCapZero_DoesNotAppear()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("s21_zero");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedBalanceAsync(employeeId, TargetYear, used: 25m, planned: 0m, carryoverIn: 0m); // fully consumed

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/transfer-agreements-needed"));
        Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
        Assert.DoesNotContain(doc.RootElement.GetProperty("cannotCompute").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    /// <summary>HRP-010 pin (4): an employee with an ACTIVE (non-REVERSED) settlement row for
    /// (VACATION, 2024) does not appear — the ferieår is already settled, so a §21 agreement is
    /// moot. RED if the candidates SQL's settlement anti-join were dropped, or if it admitted a
    /// REVERSED row as "still active".</summary>
    [Fact]
    public async Task TransferAgreementsNeeded_ActiveSettlement_DoesNotAppear()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("s21_settled");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedSettlementRowAsync(employeeId, TargetYear, sequence: 1, state: "SETTLED", trigger: "YEAR_END", crystallizedDays: null, forfeitDays: 0m);

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/transfer-agreements-needed"));
        Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
        Assert.DoesNotContain(doc.RootElement.GetProperty("cannotCompute").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    /// <summary>HRP-010 pin (5): a leaver (employment_end_date before F) does not appear, regardless
    /// of any underlying balance. RED if the candidates SQL's <c>employment_end_date &gt;= @today</c>
    /// term were dropped or replaced with an <c>is_active</c> check.</summary>
    [Fact]
    public async Task TransferAgreementsNeeded_Leaver_DoesNotAppear()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("s21_leaver");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await MarkLeaverAsync(employeeId, F.AddMonths(-4));

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/transfer-agreements-needed"));
        Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    /// <summary>
    /// HRP-010 pin (6), RE-SEEDED in S140 / TASK-14012: an employee hired DURING the target ferieår
    /// whose valuation FAILS CLOSED lands in <c>cannotCompute</c>, NOT in the needed list.
    ///
    /// <para><b>The shape, and why it is this shape.</b> The hire date
    /// (<see cref="HiredDuringTargetFerieaar"/>, 1 Jan 2025) sits INSIDE ferieår 2024
    /// (1 Sep 2024 – 31 Aug 2025), so this employee can legitimately hold part of that ferieår —
    /// the §21 question genuinely applies to them. Their dated <c>user_agreement_codes</c> /
    /// <c>employee_profiles</c> history is dated from that same hire, so it does NOT cover the
    /// ferieår START (1 Sep 2024); the settlement service's dated read at the ferieår start
    /// therefore finds no covering row and throws (<c>VacationSettlementService.cs</c> ~:1495-1498, no
    /// fallback on that read). That throw is a REAL signal — missing dated history for someone the
    /// rule applies to — and must stay visible on the tile.
    /// </para>
    ///
    /// <para><b>What TASK-14012 changed here, stated plainly.</b> Before this task the fact left
    /// <c>users.employment_start_date</c> NULL (the <c>RegressionSeed</c> default, ADR-040 D2
    /// "unbounded") and so pinned only "broken dated history ⇒ cannotCompute", saying nothing about
    /// the hire date. TASK-14012 makes the hire date decide whether the §21 question APPLIES at all,
    /// so the fact now states its hire date explicitly and names which side of the boundary it is
    /// on. The cannotCompute assertion itself is UNCHANGED — this fact asserts exactly what it
    /// asserted before, now for an employee whose applicability is explicit rather than incidental.
    /// The complementary NOT-APPLICABLE case is its own fact,
    /// <see cref="TransferAgreementsNeeded_HiredAfterFerieaarAccrualEnd_InNeitherList"/>.
    /// </para>
    ///
    /// <para><b>RED conditions.</b> (i) RED if the per-employee try/catch in
    /// <c>HrFollowUpSettlementReadRepository.GetTransferAgreementsNeededAsync</c> were removed — one
    /// bad history would 500 the whole list instead of degrading one row. (ii) RED if a caught
    /// failure were silently dropped instead of reported. (iii) RED if TASK-14012's population term
    /// were mis-bounded at the ferieår START (<c>EntitlementPeriod.AccrualStart</c>) instead of its
    /// END (<c>AccrualEnd</c>), or written with the comparison reversed: this employee would then be
    /// excluded from the population and VANISH from <c>cannotCompute</c> — suppressing a real signal
    /// in the name of removing noise, which is the one way this fix could go wrong.</para>
    /// </summary>
    [Fact]
    public async Task TransferAgreementsNeeded_HiredDuringFerieaar_ValuationFailsClosed_ReportedAsCannotCompute()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("s21_failclosed");
        // Hired 1 Jan 2025 — INSIDE ferieår 2024, so the §21 question applies — but the dated
        // history starts at that same hire, AFTER the ferieår-2024 start (1 Sep 2024), so the dated
        // agreement-code read at the ferieår start finds no covering row and CaptureSnapshotAsync
        // throws (VacationSettlementService.cs ~:1495-1498, no fallback on this specific read).
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgA, effectiveFrom: HiredDuringTargetFerieaar);
        await SetEmploymentStartAsync(employeeId, HiredDuringTargetFerieaar);

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/transfer-agreements-needed"));
        Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
        var cc = Assert.Single(doc.RootElement.GetProperty("cannotCompute").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
        Assert.Equal("VALUATION_FAILED", cc.GetProperty("reason").GetString());
        Assert.True(doc.RootElement.GetProperty("cannotComputeCount").GetInt32() >= 1);
    }

    /// <summary>
    /// HRP-010 pin (9), NEW in S140 / TASK-14012 — the NOT-APPLICABLE case: an employee whose
    /// employment began AFTER the target ferieår finished accruing appears in NEITHER list. They are
    /// out of scope for the §21 question, which is a different thing from a computation failure.
    ///
    /// <para><b>The shape.</b> The hire date (<see cref="HiredAfterTargetFerieaar"/>, 15 Sep 2025)
    /// is after ferieår 2024's accrual end (<see cref="Section21AccrualEnd"/>, 31 Aug 2025) and
    /// before the anchor F (12 Nov 2025) — so every PRE-EXISTING population term admits them
    /// (they are employed today, no leaver end date, no settlement row, no recorded agreement);
    /// only TASK-14012's accrual-end term excludes them. They cannot hold a single day of ferieår
    /// 2024, so there is nothing for HR to agree and nothing that failed to compute.</para>
    ///
    /// <para><b>RED condition (reasoned from the source — Docker is unavailable locally, so this
    /// first executes in CI).</b> Delete the candidates SQL's
    /// <c>employment_start_date &lt;= @accrualEnd</c> term (or stop threading
    /// <c>period.AccrualEnd</c> into it) and this fact goes RED: the employee re-enters the
    /// population, the valuation's dated agreement-code read at the ferieår START (1 Sep 2024) finds
    /// no covering row — their history is dated from the 15 Sep 2025 hire — it throws, and they
    /// REAPPEAR in <c>cannotCompute</c> with reason <c>VALUATION_FAILED</c>, taking
    /// <c>cannotComputeCount</c> from 0 to 1. That is exactly the defect TASK-14012 fixed: the tile
    /// reporting "could not compute" over employees the process does not apply to, which on a young
    /// dataset is most of the workforce.</para>
    /// </summary>
    [Fact]
    public async Task TransferAgreementsNeeded_HiredAfterFerieaarAccrualEnd_InNeitherList()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("s21_hired_after");
        // Dated history anchored at the hire date, exactly as a real employee's is — that is what
        // makes the RED condition above bite: without the accrual-end term this employee is valued,
        // the dated read at 1 Sep 2024 finds nothing, and they surface as cannotCompute.
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgA, effectiveFrom: HiredAfterTargetFerieaar);
        await SetEmploymentStartAsync(employeeId, HiredAfterTargetFerieaar);

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/transfer-agreements-needed"));

        // The window IS open at F, so the response is a real read of the population — not the
        // out-of-season early return, which would empty both lists for the wrong reason.
        Assert.True(doc.RootElement.GetProperty("windowOpen").GetBoolean());
        Assert.Equal(TargetYear, doc.RootElement.GetProperty("entitlementYear").GetInt32());

        Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
        Assert.DoesNotContain(doc.RootElement.GetProperty("cannotCompute").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);

        // This employee is the ONLY member of OrgA in this fact's own container (a fresh Postgres
        // per fact), so both counts are exactly zero — the sharpest form of "in neither list".
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("cannotComputeCount").GetInt32());
    }

    /// <summary>HRP-010 pin (7): at an October anchor (before the 1 Nov reminder window opens) the
    /// response carries <c>windowOpen: false</c> with the correct <c>windowOpensOn</c>, an EMPTY item
    /// list and a zero cannotCompute count — regardless of any seeded candidate. RED if the window
    /// gate (<c>today &gt;= windowOpensOn</c>) were removed, or if the out-of-season branch still ran
    /// the per-candidate valuation (defeating the whole point of the early return).</summary>
    [Fact]
    public async Task TransferAgreementsNeeded_OctoberAnchor_WindowClosed()
    {
        var october = new DateOnly(2025, 10, 15);
        using var host = _factory.WithFixedToday(october);
        using var client = host.CreateClient();

        var employeeId = NextId("s21_october");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SeedBalanceAsync(employeeId, TargetYear, used: 0m, planned: 0m, carryoverIn: 0m); // would qualify if the window were open

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/transfer-agreements-needed"));
        Assert.False(doc.RootElement.GetProperty("windowOpen").GetBoolean());
        Assert.Equal(new DateOnly(2025, 11, 1), ReadDate(doc.RootElement.GetProperty("windowOpensOn")));
        Assert.Equal(TargetYear, doc.RootElement.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        Assert.Empty(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, doc.RootElement.GetProperty("cannotComputeCount").GetInt32());
    }

    // ════════════════════════════════════════════════════════════════════════
    // Payout-pending (pre-existing endpoint) — seeded with a qualifying row AND a reconciled
    // near-miss, so the count cannot pass vacuously on two zeroes.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The EXISTING <c>GET /api/vacation-settlements/payout-pending</c> endpoint, seeded (refinement
    /// B3's "payout-pending (1, seeded)"). One qualifying row (SETTLED, payout_days &gt; 0,
    /// <c>payout_reconciled_at IS NULL</c>) and one reconciled near-miss (same shape, but
    /// <c>payout_reconciled_at</c> set) — a bare <c>count == 0</c> comparison would pass vacuously if
    /// BOTH rows were absent from the seed; this pin proves the reconciled row is excluded BY the
    /// predicate, not by its own absence. RED if the <c>payout_reconciled_at IS NULL</c> term were
    /// dropped from <c>VacationSettlementEndpoints.MapPayoutPendingList</c>'s SQL.
    /// </summary>
    [Fact]
    public async Task PayoutPending_SeededQualifyingRowAndReconciledNearMiss_CountExact()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var qualifying = NextId("payout_q");
        var reconciled = NextId("payout_r");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, qualifying, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, reconciled, OrgA);
        await SeedPayoutRowAsync(qualifying, TargetYear, payoutDays: 4.0m, reconciledAt: null);
        await SeedPayoutRowAsync(reconciled, TargetYear, payoutDays: 6.0m, reconciledAt: AtUtcMidnight(F.AddDays(-1)));

        var hr = Client(host, HrToken(OrgA));
        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/vacation-settlements/payout-pending"));
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("employeeId").GetString() == qualifying || i.GetProperty("employeeId").GetString() == reconciled)
            .ToList();
        var item = Assert.Single(items);
        Assert.Equal(qualifying, item.GetProperty("employeeId").GetString());
        Assert.Equal(4.0m, item.GetProperty("payoutDays").GetDecimal());
    }

    // ─────────────────────────────── HTTP / JWT helpers ───────────────────────────────

    private static HttpClient Client(WebApplicationFactory<Program> host, string token)
    {
        var c = host.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private static string HrToken(string orgId) => NewTokenService().GenerateToken(
        employeeId: "hr_s140_actor", name: "hr_s140_actor", role: StatsTidRoles.LocalHR, agreementCode: "AC",
        orgId: orgId, scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });

    private static string GlobalAdminToken() => NewTokenService().GenerateToken(
        employeeId: "hr_s140_admin", name: "hr_s140_admin", role: StatsTidRoles.GlobalAdmin, agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    /// <summary>Passes the HROrAbove POLICY (role = LocalHR clears the role check, and the one
    /// scope's Role also clears the policy's role-membership check) but yields an EMPTY accessible
    /// set from <c>OrgScopeValidator.GetAccessibleOrgsAsync</c>: its ScopeType is
    /// <c>ORG_AND_DESCENDANTS</c>, the S93-dropped legacy type that contributes nothing to the
    /// accessible-org union (only <c>GLOBAL</c> / <c>ORG_ONLY</c> do).</summary>
    private static string NoContributingScopeHrToken() => NewTokenService().GenerateToken(
        employeeId: "hr_s140_noscope", name: "hr_s140_noscope", role: StatsTidRoles.LocalHR, agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, OrgA, "ORG_AND_DESCENDANTS") });

    // ─────────────────────────────── settlement-service driver ───────────────────────────────

    private static async Task<SettlementOutcome> SettleAsync(
        DbConnectionFactory dbFactory, VacationSettlementService service, string employeeId, int year, string trigger)
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        try
        {
            var outcome = await service.SettleAsync(employeeId, VacationType, year, trigger, conn, tx);
            await tx.CommitAsync();
            return outcome;
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync();
            throw;
        }
    }

    // ─────────────────────────────── seed helpers ───────────────────────────────

    private static DateTimeOffset AtUtcMidnight(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>Parses a wire <c>DateOnly</c> (an ISO <c>"yyyy-MM-dd"</c> string) from a
    /// <see cref="JsonElement"/> — used instead of a version-sensitive <c>GetDateOnly()</c> call.</summary>
    private static DateOnly ReadDate(JsonElement e) => DateOnly.Parse(e.GetString()!);

    /// <summary>
    /// Sets the HR-managed hire date (<c>users.employment_start_date</c>) — the column TASK-14012's
    /// §21 population term reads. <see cref="RegressionSeed"/> deliberately leaves it NULL (ADR-040
    /// D2: NULL means employed since the beginning of time, so no fixture backfill was ever needed),
    /// which means a fact whose meaning depends on a real hire date must state one EXPLICITLY —
    /// leaving it NULL would pin the unbounded case while appearing to pin a dated one.
    /// </summary>
    private async Task SetEmploymentStartAsync(string employeeId, DateOnly startDate) =>
        await ExecAsync(
            "UPDATE users SET employment_start_date = @p1, updated_at = NOW() WHERE user_id = @p0",
            employeeId, startDate);

    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate) =>
        await ExecAsync(
            "UPDATE users SET employment_end_date = @p1, is_active = FALSE, end_date_deactivated = TRUE, updated_at = NOW() WHERE user_id = @p0",
            employeeId, endDate);

    private async Task SeedBalanceAsync(string employeeId, int year, decimal used, decimal planned, decimal carryoverIn) =>
        await ExecAsync(
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used, planned, carryover_in, updated_at)
            VALUES (gen_random_uuid(), @p0, @p1, @p2, 25, @p3, @p4, @p5, NOW())
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
                DO UPDATE SET used = EXCLUDED.used, planned = EXCLUDED.planned, carryover_in = EXCLUDED.carryover_in
            """, employeeId, VacationType, year, used, planned, carryoverIn);

    /// <summary>General settlement-row seed (HRP-005 rows: PENDING_REVIEW / SETTLED, YEAR_END).</summary>
    private async Task SeedSettlementRowAsync(
        string employeeId, int year, int sequence, string state, string trigger,
        decimal? crystallizedDays, decimal forfeitDays, DateTimeOffset? createdAt = null)
    {
        var snapshotJson = BuildSnapshotJson(year, crystallizedDays);
        await ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence, settlement_state, trigger,
                 snapshot, transfer_days, payout_days, forfeit_days, review_disposition, version, created_at)
            VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6::jsonb, 0, 0, @p7, NULL, 1, COALESCE(@p8, NOW()))
            """, employeeId, VacationType, year, sequence, state, trigger, snapshotJson, forfeitDays,
            (object?)createdAt ?? DBNull.Value);
    }

    /// <summary>HRP-007-shaped settlement row: TERMINATION, with an explicit
    /// <c>settlementBoundaryDate</c> in the snapshot — required (non-default) for
    /// <c>HrFollowUpSettlementReadRepository.ReadRequestableQuantity</c> to accept the row.</summary>
    private async Task SeedTerminationSettlementRowAsync(
        string employeeId, int year, int sequence, string state, decimal crystallizedDays,
        string? reviewDisposition = null, DateTimeOffset? createdAt = null)
    {
        var boundary = new DateOnly(year + 1, 2, 28);
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 12.5m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            agreementCode = "AC",
            transferAgreementDays = 0m,
            isFeriehindret = false,
            terminationDate = boundary.ToString("yyyy-MM-dd"),
            crystallizationBasis = "S26_WHOLE_MONTH",
            crystallizedDays,
            settlementBoundaryDate = boundary.ToString("yyyy-MM-dd"),
        });
        // vacation_settlements_claim_disposition_paired CHECK: claim_disposition_days must be
        // NON-NULL exactly when review_disposition IN ('MODREGNING','WAIVED') — WAIVED (the only
        // disposition this file seeds here) needs a value, anything else must leave it NULL.
        var claimDispositionDays = string.Equals(reviewDisposition, "WAIVED", StringComparison.Ordinal)
            || string.Equals(reviewDisposition, "MODREGNING", StringComparison.Ordinal)
            ? crystallizedDays
            : (decimal?)null;

        await ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence, settlement_state, trigger,
                 snapshot, transfer_days, payout_days, forfeit_days, review_disposition,
                 claim_disposition_days, version, created_at)
            VALUES (@p0, @p1, @p2, @p3, @p4, 'TERMINATION', @p5::jsonb, 0, 0, 0, @p6, @p7, 1, COALESCE(@p8, NOW()))
            """, employeeId, VacationType, year, sequence, state, snapshotJson,
            (object?)reviewDisposition ?? DBNull.Value, (object?)claimDispositionDays ?? DBNull.Value,
            (object?)createdAt ?? DBNull.Value);
    }

    private static string BuildSnapshotJson(int year, decimal? crystallizedDays) => JsonSerializer.Serialize(new
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
        agreementCode = "AC",
        transferAgreementDays = 0m,
        isFeriehindret = false,
        crystallizedDays,
    });

    private async Task SeedTerminationPayoutRequestAsync(string employeeId, int year, int sequence, string state) =>
        await ExecAsync(
            """
            INSERT INTO termination_payout_requests
                (employee_id, entitlement_type, entitlement_year, settlement_sequence, state, request_date, recorded_by, version)
            VALUES (@p0, @p1, @p2, @p3, @p4, @p5, 'test-seed-hr', 1)
            """, employeeId, VacationType, year, sequence, state, F);

    private async Task SeedPayoutRowAsync(string employeeId, int year, decimal payoutDays, DateTimeOffset? reconciledAt) =>
        await ExecAsync(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence, settlement_state, trigger,
                 snapshot, transfer_days, payout_days, forfeit_days, review_disposition, version,
                 payout_reconciled_at, payout_reconciled_by)
            VALUES (@p0, @p1, @p2, 1, 'SETTLED', 'YEAR_END', '{}'::jsonb, 0, @p3, 0, NULL, 1, @p4, @p5)
            """, employeeId, VacationType, year, payoutDays,
            (object?)reconciledAt ?? DBNull.Value,
            // vacation_settlements_payout_reconciled_paired CHECK: the two columns are NULL / NOT NULL
            // together — a reconciled row needs a recorder, an unreconciled one must have neither.
            reconciledAt is null ? (object)DBNull.Value : "test-seed-hr");

    // ─────────────────────────────── polling (real elapsed time — the HTTP-polling exception) ───

    private sealed record RawEvent(Guid EventId, DateTimeOffset OccurredAt);

    /// <summary>
    /// Polls the canonical <c>events</c> table for the outbox publisher to drain
    /// <paramref name="expectedCount"/> rows of <paramref name="eventType"/> for this employee's
    /// stream. Uses REAL elapsed wall-clock time (the one place this suite's fixed business clock
    /// does not apply — this measures publisher latency, not a business date) with a generous
    /// timeout; the publisher polls every 250 ms while busy.
    /// </summary>
    private async Task<IReadOnlyList<RawEvent>> WaitForEventsAsync(
        string employeeId, string eventType, int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var rows = await ReadEventsAsync(employeeId, eventType);
            if (rows.Count >= expectedCount)
                return rows;
            await Task.Delay(200);
        }
        var final = await ReadEventsAsync(employeeId, eventType);
        Assert.Fail(
            $"Expected {expectedCount} '{eventType}' event(s) for stream containing {employeeId} within " +
            $"{timeout.TotalSeconds}s; found {final.Count}. The outbox publisher may not have drained in time.");
        return final; // unreachable
    }

    private async Task<IReadOnlyList<RawEvent>> ReadEventsAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id, occurred_at FROM events
            WHERE event_type = @p1 AND data->>'employeeId' = @p0
            ORDER BY occurred_at ASC
            """, conn);
        cmd.Parameters.AddWithValue("p0", employeeId);
        cmd.Parameters.AddWithValue("p1", eventType);
        var result = new List<RawEvent>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new RawEvent(
                reader.GetGuid(0),
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc))));
        return result;
    }

    // ─────────────────────────────── raw DB helpers ───────────────────────────────

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // compile-time test constant; values bound via parameters below
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }
}
