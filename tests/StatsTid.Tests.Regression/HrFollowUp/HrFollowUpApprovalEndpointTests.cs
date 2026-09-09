using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.HrFollowUp;

/// <summary>
/// S140 / TASK-14005 (refinement B3) — Docker-gated pins for the APPROVAL / LIFECYCLE /
/// ORGANISATION HR follow-up reads (<see cref="HrFollowUpApprovalEndpoints"/>, TASK-14004; HRP-011,
/// HRP-012, HRP-013, HRP-014, HRP-015, HRP-022, and the <c>?summary=true</c> contract on two of the
/// five reads).
///
/// <para>
/// <b>RED-FIRST, reasoned from the spec.</b> Docker is unavailable on the authoring machine, so
/// every pin below is derived from <c>HrFollowUpApprovalEndpoints.cs</c> /
/// <c>HrFollowUpApprovalReadRepository.cs</c> / <c>DelegationExpiryService.cs</c> as read, not from
/// an observed run — these first execute (and are CI-verified, never claimed green locally) in the
/// sprint-close watched CI run. Each fact's doc comment states the RED condition.
/// </para>
///
/// <para>
/// <b>PAT-008 — one fixed anchor, one WAF host per fact.</b> <c>F = 2025-11-12</c> (Wednesday,
/// OK24 side), the SAME anchor as the settlement-family sibling suite. Every fact derives its own
/// host via <c>_factory.WithFixedToday(F)</c> and boots it exactly once. This matters MORE here
/// than in the settlement suite: <see cref="DelegationExpiryService"/> is a hosted
/// <c>BackgroundService</c> that fires an sweep pass immediately on host start and every 5 minutes
/// after, reading the SAME injected <c>TimeProvider</c> the host's other consumers do — a second
/// booted host would run a second, independent sweeper against the same container and could close a
/// seeded row before the test's own manual sweep call runs.
/// </para>
///
/// <para>
/// <b>Event-backed reads seed through the REAL emit path.</b> HRP-014's expired-delegation events
/// are produced by actually running <see cref="DelegationExpiryService.CloseExpiredDelegationsAsync"/>
/// (grabbed from the host's own <see cref="IHostedService"/> registration — the SAME running
/// instance, called for a deterministic single-shot pass instead of waiting on its 5-minute timer)
/// or the real <c>DELETE /api/admin/reporting-lines/{'{'}managerId{'}'}/vikar</c> endpoint for a manual
/// revoke — never a direct <c>INSERT</c> into <c>events</c>. <c>DomainEventBase.OccurredAt</c> is
/// stamped from the REAL wall clock (a separate code path from the injected business-date seam), so
/// the exact 30-vs-31-day boundary pin backdates the emitted event's <c>occurred_at</c> via a plain
/// <c>UPDATE</c> after the publisher has drained it — a declared, deliberate calibration for
/// determinism, not a fabricated event.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class HrFollowUpApprovalEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string OrgA = "STY_HRFU_APR_A";
    private const string OrgForeign = "STY_HRFU_APR_F";
    private const string OrgCurrent = "STY_HRFU_APR_CUR";
    private const string OrgStamped = "STY_HRFU_APR_STA";

    /// <summary>Wednesday, OK24 side — the one fixed "today" for every fact in this class (matches
    /// the settlement-family sibling suite's anchor).</summary>
    private static readonly DateOnly F = new(2025, 11, 12);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Deliberately NOT booted here (PAT-008 "one host per fact").
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
    // Org scope — empty accessible set ⇒ 403, across all five reads.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Same construction as the settlement-family sibling: a token that clears the
    /// <c>HROrAbove</c> POLICY (role = LocalHR, and one scope whose Role also clears the policy's
    /// role-membership check) but yields an EMPTY accessible-org set because that scope's
    /// <c>ScopeType</c> is <c>ORG_AND_DESCENDANTS</c> — the S93-dropped legacy type
    /// <c>OrgScopeValidator.GetAccessibleOrgsAsync</c> discards. RED if any handler's
    /// <c>accessibleOrgIds is {'{'} Count: 0 {'}'}</c> branch were deleted or changed to return an
    /// empty 200.</summary>
    [Fact]
    public async Task OrgScope_ActorWithNoContributingScope_403_AcrossAllFiveReads()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();
        var noScope = Client(host, NoContributingScopeHrToken());

        foreach (var url in new[]
                 {
                     "/api/hr/follow-up/past-deadline",
                     "/api/hr/follow-up/leaver-final-month",
                     "/api/hr/follow-up/approved-not-exported",
                     "/api/hr/follow-up/uncovered-approvers",
                     "/api/hr/follow-up/cannot-register",
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
    // HRP-012 — GET /api/hr/follow-up/past-deadline
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The cross-org DIVERGENCE pin (task hard rule): the row's STAMPED <c>approval_periods.org_id</c>
    /// (OrgStamped) differs from the employee's CURRENT <c>users.primary_org_id</c> (OrgCurrent). RED
    /// if the read ever filtered on <c>ap.org_id</c> instead of <c>u.primary_org_id</c> — that mistake
    /// would flip which HR sees the row, and a naive same-org seed could never catch it.
    /// </summary>
    [Fact]
    public async Task PastDeadline_CrossOrgDivergence_UsesEmployeesCurrentOrg_NotStampedApprovalPeriodOrg()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("pd_divergent");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgCurrent);
        await SeedApprovalPeriodAsync(employeeId, OrgStamped, 2025, 11, "DRAFT", employeeDeadline: F.AddDays(-1));

        using var currentDoc = JsonDocument.Parse(await Client(host, HrToken(OrgCurrent)).GetStringAsync("/api/hr/follow-up/past-deadline"));
        Assert.Contains(currentDoc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId && i.GetProperty("month").GetInt32() == 11);

        using var stampedDoc = JsonDocument.Parse(await Client(host, HrToken(OrgStamped)).GetStringAsync("/api/hr/follow-up/past-deadline"));
        Assert.DoesNotContain(stampedDoc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    /// <summary>HRP-012 pin: the no-row boundary. A never-sent month has NO stored deadline, so the
    /// pin is "the previous month (computed employee deadline 2 days after its end, already passed)
    /// is late; the current month (computed deadline still ahead) is not" — never a single ±1 day
    /// anchor, since no row exists to hold one. RED if the enumeration's floor/ceiling or the
    /// computed-fallback expression (<c>month_end + submitDays</c>) changed.</summary>
    [Fact]
    public async Task PastDeadline_NoRowBoundary_PreviousMonthLate_CurrentMonthNot()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("pd_norow");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(2025, 10, 1)); // only Oct/Nov are in-window

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/past-deadline"));
        var october = Assert.Single(doc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId && i.GetProperty("year").GetInt32() == 2025 && i.GetProperty("month").GetInt32() == 10);
        Assert.Equal("NONE", october.GetProperty("periodStatus").GetString());
        Assert.Equal("computed", october.GetProperty("deadlineSource").GetString());
        Assert.Equal(new DateOnly(2025, 11, 2), ReadDate(october.GetProperty("ageAnchor"))); // 31 Oct + 2

        Assert.DoesNotContain(doc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId && i.GetProperty("year").GetInt32() == 2025 && i.GetProperty("month").GetInt32() == 11);
    }

    /// <summary>HRP-012 pin: the exact ±1 day boundary on a STORED DRAFT deadline. RED if the SQL's
    /// <c>&lt;</c> comparison were <c>&lt;=</c> (the F row would wrongly appear) or the reverse (the
    /// F−1 row would wrongly be absent).</summary>
    [Fact]
    public async Task PastDeadline_StoredDraftDeadline_ExactPlusMinusOneDayBoundary()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var lateEmployee = NextId("pd_draft_late");
        var onTimeEmployee = NextId("pd_draft_ontime");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, lateEmployee, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, onTimeEmployee, OrgA);
        // Window tightened to November ONLY — otherwise both employees' unbounded history would
        // also surface as "never sent" (computed employee-late) for every earlier month in the
        // rolling floor, and the single-item assertions below would see more than one match.
        await SetEmploymentStartDateAsync(lateEmployee, new DateOnly(2025, 11, 1));
        await SetEmploymentStartDateAsync(onTimeEmployee, new DateOnly(2025, 11, 1));
        await SeedApprovalPeriodAsync(lateEmployee, OrgA, 2025, 11, "DRAFT", employeeDeadline: F.AddDays(-1));
        await SeedApprovalPeriodAsync(onTimeEmployee, OrgA, 2025, 11, "DRAFT", employeeDeadline: F);

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/past-deadline"));
        var late = Assert.Single(doc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == lateEmployee);
        Assert.Equal("stored", late.GetProperty("deadlineSource").GetString());
        Assert.Equal(F.AddDays(-1), ReadDate(late.GetProperty("ageAnchor")));
        Assert.DoesNotContain(doc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == onTimeEmployee);
    }

    /// <summary>HRP-012 pin: SUBMITTED with a stored manager_deadline in the past is APPROVER late
    /// (never employee late). RED if SUBMITTED/EMPLOYEE_APPROVED were removed from the approver-late
    /// status set.</summary>
    [Fact]
    public async Task PastDeadline_SubmittedManagerDeadline_ApproverLate_NotEmployeeLate()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("pd_submitted");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        // Window tightened to November — otherwise the unbounded history's earlier "never sent"
        // months would also surface as employee-late, and the DoesNotContain below (which checks
        // ANY month for this employee, not just November) would fail for an unrelated reason.
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(2025, 11, 1));
        await SeedApprovalPeriodAsync(employeeId, OrgA, 2025, 11, "SUBMITTED", managerDeadline: F.AddDays(-1));

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/past-deadline"));
        var item = Assert.Single(doc.RootElement.GetProperty("approverLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
        Assert.Equal("stored", item.GetProperty("deadlineSource").GetString());
        Assert.DoesNotContain(doc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    /// <summary>
    /// HRP-012 pin: a REJECTED month counts as employee-late ONLY once month-end + 2 has passed —
    /// aged from THAT deadline, never "instantly" from the rejection. Employee1's REJECTED October
    /// row (computed deadline 2 Nov, already past F) appears; Employee2's REJECTED November row
    /// (computed deadline 2 Dec, not yet past F) does NOT — proving the rule is NOT "REJECTED always
    /// counts". RED if REJECTED were treated as unconditionally late regardless of the deadline.
    /// </summary>
    [Fact]
    public async Task PastDeadline_Rejected_CountsLateOnlyOncePastDeadline_AgedFromDeadlineNotRejection()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var pastDeadline = NextId("pd_rejected_past");
        var notYetDue = NextId("pd_rejected_notyet");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, pastDeadline, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, notYetDue, OrgA);
        // Tight windows so each employee's ONLY candidate month is the one under test — otherwise
        // the unbounded default history would surface earlier "never sent" months too.
        await SetEmploymentStartDateAsync(pastDeadline, new DateOnly(2025, 10, 1));
        await SetEmploymentStartDateAsync(notYetDue, new DateOnly(2025, 11, 1));
        await SeedApprovalPeriodAsync(pastDeadline, OrgA, 2025, 10, "REJECTED"); // no stored deadline ⇒ computed
        await SeedApprovalPeriodAsync(notYetDue, OrgA, 2025, 11, "REJECTED");

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/past-deadline"));
        var late = Assert.Single(doc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == pastDeadline);
        Assert.Equal("computed", late.GetProperty("deadlineSource").GetString());
        Assert.Equal(new DateOnly(2025, 11, 2), ReadDate(late.GetProperty("ageAnchor")));
        Assert.DoesNotContain(doc.RootElement.GetProperty("employeeLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == notYetDue);
    }

    /// <summary>HRP-012 pin: a NULL manager_deadline on a SUBMITTED row older than month-end + 5 is
    /// approver-late with <c>deadlineSource = "computed"</c>. RED if the computed fallback
    /// (<c>month_end + approveDays</c>, <c>InstitutionalDeadlines.ApproveDays = 5</c>) were dropped
    /// and a NULL deadline were silently treated as "on time".</summary>
    [Fact]
    public async Task PastDeadline_NullDeadlineSubmitted_ApproverLate_ComputedSource()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("pd_null_submitted");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await SetEmploymentStartDateAsync(employeeId, new DateOnly(2025, 10, 1));
        await SeedApprovalPeriodAsync(employeeId, OrgA, 2025, 10, "SUBMITTED"); // no stored manager_deadline

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/past-deadline"));
        var item = Assert.Single(doc.RootElement.GetProperty("approverLate").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
        Assert.Equal("computed", item.GetProperty("deadlineSource").GetString());
        Assert.Equal(new DateOnly(2025, 11, 5), ReadDate(item.GetProperty("ageAnchor"))); // 31 Oct + 5
    }

    // ════════════════════════════════════════════════════════════════════════
    // HRP-011 — GET /api/hr/follow-up/leaver-final-month
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// HRP-011 pins (3): a leaver's FINAL month with no row appears, anchored to the computed
    /// MANAGER deadline (month-end + 5, not the employee one); the same month APPROVED does not
    /// appear; and an EARLIER never-sent month of the same leaver belongs to HRP-012 only (absent
    /// from THIS list). RED conditions: dropping the <c>period_status &lt;&gt; 'APPROVED'</c> term
    /// (the APPROVED sibling would wrongly appear); or dropping the "final month" containment terms
    /// (<c>employment_end_date BETWEEN month_start AND month_end</c>), which would let the earlier
    /// never-sent month leak onto this list too.
    /// </summary>
    [Fact]
    public async Task LeaverFinalMonth_NoRow_Appears_ApprovedExcludes_EarlierMonthStaysOffThisList()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("lfm_norow");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);
        await MarkLeaverAsync(employeeId, new DateOnly(2025, 9, 20)); // final month = September 2025

        var hr = Client(host, HrToken(OrgA));
        using (var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/leaver-final-month")))
        {
            var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray(),
                i => i.GetProperty("employeeId").GetString() == employeeId);
            Assert.Equal(2025, item.GetProperty("year").GetInt32());
            Assert.Equal(9, item.GetProperty("month").GetInt32());
            Assert.Equal("NONE", item.GetProperty("periodStatus").GetString());
            Assert.Equal("computed", item.GetProperty("deadlineSource").GetString());
            Assert.Equal(new DateOnly(2025, 10, 5), ReadDate(item.GetProperty("ageAnchor"))); // 30 Sep + 5 (manager deadline)
        }

        // The earlier never-sent month (August) is a HRP-012 item, not a HRP-011 item.
        using (var pastDeadlineDoc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/past-deadline")))
        {
            Assert.Contains(pastDeadlineDoc.RootElement.GetProperty("employeeLate").EnumerateArray(),
                i => i.GetProperty("employeeId").GetString() == employeeId && i.GetProperty("month").GetInt32() == 8);
        }
        using (var leaverDoc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/leaver-final-month")))
        {
            Assert.DoesNotContain(leaverDoc.RootElement.GetProperty("items").EnumerateArray(),
                i => i.GetProperty("employeeId").GetString() == employeeId && i.GetProperty("month").GetInt32() == 8);
        }

        // A second leaver whose final month IS approved: absent from HRP-011 (this test's own pin);
        // the HRP-022 half of the same fixture is asserted in ApprovedNotExported_* below.
        var approvedLeaver = NextId("lfm_approved");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, approvedLeaver, OrgA);
        await MarkLeaverAsync(approvedLeaver, new DateOnly(2025, 8, 15));
        await SeedApprovalPeriodAsync(approvedLeaver, OrgA, 2025, 8, "APPROVED");

        using var finalDoc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/leaver-final-month"));
        Assert.DoesNotContain(finalDoc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == approvedLeaver);
    }

    // ════════════════════════════════════════════════════════════════════════
    // HRP-022 — GET /api/hr/follow-up/approved-not-exported
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// HRP-022 pins (2): an APPROVED month with no <c>payroll_export_records</c> row appears; the
    /// same shape WITH an export row does not. Also proves the HRP-011/022 relationship directly: a
    /// leaver's APPROVED final month (excluded from HRP-011 above) DOES appear here — the two lists
    /// are disjoint by status but not complementary (different base populations). RED if the
    /// <c>NOT EXISTS (payroll_export_records ...)</c> anti-join were dropped, or the
    /// <c>period_status = 'APPROVED'</c> term were removed.
    /// </summary>
    [Fact]
    public async Task ApprovedNotExported_NoExportRow_Appears_WithExportRow_Absent_LeaversApprovedMonthAlsoAppearsHere()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var unexported = NextId("ane_unexported");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, unexported, OrgA);
        await SeedApprovalPeriodAsync(unexported, OrgA, 2025, 10, "APPROVED");

        var exported = NextId("ane_exported");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, exported, OrgA);
        await SeedApprovalPeriodAsync(exported, OrgA, 2025, 10, "APPROVED");
        await SeedPayrollExportRecordAsync(exported, 2025, 10);

        var approvedLeaver = NextId("ane_leaver_approved");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, approvedLeaver, OrgA);
        await MarkLeaverAsync(approvedLeaver, new DateOnly(2025, 8, 15));
        await SeedApprovalPeriodAsync(approvedLeaver, OrgA, 2025, 8, "APPROVED");

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/approved-not-exported"));
        Assert.Contains(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == unexported);
        Assert.DoesNotContain(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == exported);
        Assert.Contains(doc.RootElement.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == approvedLeaver);
    }

    // ════════════════════════════════════════════════════════════════════════
    // HRP-013 / HRP-014 — GET /api/hr/follow-up/uncovered-approvers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>HRP-013 pin: an orphan (no structural PRIMARY edge either way — the default shape of
    /// a freshly-seeded employee, since <see cref="RegressionSeed"/> writes no reporting-line rows)
    /// appears for their own org's HR and not for a foreign org's. RED if the orphan roll-up's
    /// <c>NOT EXISTS</c> pair were narrowed to only the "no approver" half.</summary>
    [Fact]
    public async Task UncoveredApprovers_Orphan_ScopedToOwnOrg_NotForeign()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("orphan");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);

        using var ownDoc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        Assert.Contains(ownDoc.RootElement.GetProperty("orphans").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);

        using var foreignDoc = JsonDocument.Parse(await Client(host, HrToken(OrgForeign)).GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        Assert.DoesNotContain(foreignDoc.RootElement.GetProperty("orphans").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);

        // A GlobalAdmin sees across organisations — the null accessible-orgs sentinel bypass.
        using var adminDoc = JsonDocument.Parse(await Client(host, GlobalAdminToken()).GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        Assert.Contains(adminDoc.RootElement.GetProperty("orphans").EnumerateArray(),
            i => i.GetProperty("employeeId").GetString() == employeeId);
    }

    /// <summary>
    /// The cross-org DIVERGENCE pin for HRP-014: the manager_vikar row's STAMPED
    /// <c>organisation_id</c> (OrgStamped) differs from the absent approver's CURRENT
    /// <c>users.primary_org_id</c> (OrgCurrent). Seeded through the REAL emit path (the actual
    /// running <see cref="DelegationExpiryService"/> instance, single-shot), never a direct INSERT
    /// into <c>events</c>. RED if the read ever joined on <c>manager_vikar.organisation_id</c>
    /// instead of the absent approver's current <c>users.primary_org_id</c>.
    /// </summary>
    [Fact]
    public async Task UncoveredApprovers_CrossOrgDivergence_UsesAbsentApproversCurrentOrg_NotStampedManagerVikarOrg()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var absentApprover = NextId("div_absent");
        var vikarUser = NextId("div_vikar");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, absentApprover, OrgCurrent);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, vikarUser, OrgCurrent);
        var vikarId = await SeedManagerVikarAsync(absentApprover, vikarUser, OrgStamped, untilDate: F.AddDays(-40));

        await RunDelegationExpirySweepAsync(host);
        await WaitForEventAsync("vikarId", vikarId.ToString(), timeout: TimeSpan.FromSeconds(15));
        await BackdateVikarEventAsync(vikarId, AtUtcMidnight(F.AddDays(-5)));

        using var currentDoc = JsonDocument.Parse(await Client(host, HrToken(OrgCurrent)).GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        Assert.Contains(currentDoc.RootElement.GetProperty("expiredDelegations").EnumerateArray(),
            i => i.GetProperty("vikarId").GetGuid() == vikarId);

        using var stampedDoc = JsonDocument.Parse(await Client(host, HrToken(OrgStamped)).GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        Assert.DoesNotContain(stampedDoc.RootElement.GetProperty("expiredDelegations").EnumerateArray(),
            i => i.GetProperty("vikarId").GetGuid() == vikarId);
    }

    /// <summary>
    /// HRP-014 pin: the exact 30-vs-31-day expiry-window boundary. Both delegations are closed by
    /// the SAME real sweep pass; the events' <c>occurred_at</c> (real wall clock at emission — a
    /// separate code path from the injected business-date seam) is backdated afterward to the exact
    /// F-relative anchor each leg needs — a declared calibration, not a fabricated event. RED if the
    /// repository's <c>ev.occurred_at &gt;= @since</c> bound (<c>@since = today − 30</c>) were an
    /// exclusive comparison (the 30-day leg would wrongly drop) or a wider one (the 31-day leg would
    /// wrongly appear).
    /// </summary>
    [Fact]
    public async Task UncoveredApprovers_ExpiredDelegation_30DaysAppears_31DaysDoesNot()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var approver30 = NextId("exp30_absent");
        var vikar30 = NextId("exp30_vikar");
        var approver31 = NextId("exp31_absent");
        var vikar31 = NextId("exp31_vikar");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, approver30, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, vikar30, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, approver31, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, vikar31, OrgA);

        var vikarId30 = await SeedManagerVikarAsync(approver30, vikar30, OrgA, untilDate: F.AddDays(-60));
        var vikarId31 = await SeedManagerVikarAsync(approver31, vikar31, OrgA, untilDate: F.AddDays(-60));

        await RunDelegationExpirySweepAsync(host);
        await WaitForEventAsync("vikarId", vikarId30.ToString(), timeout: TimeSpan.FromSeconds(15));
        await WaitForEventAsync("vikarId", vikarId31.ToString(), timeout: TimeSpan.FromSeconds(15));

        await BackdateVikarEventAsync(vikarId30, AtUtcMidnight(F.AddDays(-30))); // exactly the window floor
        await BackdateVikarEventAsync(vikarId31, AtUtcMidnight(F.AddDays(-31))); // one day OUTSIDE it

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        var included = Assert.Single(doc.RootElement.GetProperty("expiredDelegations").EnumerateArray(),
            i => i.GetProperty("vikarId").GetGuid() == vikarId30 || i.GetProperty("vikarId").GetGuid() == vikarId31);
        Assert.Equal(vikarId30, included.GetProperty("vikarId").GetGuid());
        Assert.Equal(30, included.GetProperty("daysSinceExpiry").GetInt32());
    }

    /// <summary>
    /// HRP-014 pin: a MANUALLY revoked delegation never appears, even backdated well inside the
    /// 30-day window — proving the discriminator is the event's <c>endReason</c>, not recency. Seeded
    /// through the REAL admin-revoke endpoint (<c>DELETE /api/admin/reporting-lines/{'{'}managerId{'}'}/vikar</c>),
    /// which writes <c>endReason = 'REVOKED'</c>. RED if the repository's
    /// <c>data-&gt;&gt;'endReason' = 'EXPIRED'</c> predicate were loosened to admit any
    /// <c>ManagerVikarEnded</c> regardless of reason.
    /// </summary>
    [Fact]
    public async Task UncoveredApprovers_ManuallyRevoked_NeverAppears_EvenWithinWindow()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var absentApprover = NextId("revoke_absent");
        var vikarUser = NextId("revoke_vikar");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, absentApprover, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, vikarUser, OrgA);
        // untilDate far in the FUTURE (relative to the fixed clock F, which every consumer in this
        // host — including the hosted sweeper — reads) so the automatic sweep never touches it.
        var vikarId = await SeedManagerVikarAsync(absentApprover, vikarUser, OrgA, untilDate: F.AddMonths(3));

        var hr = Client(host, HrToken(OrgA));
        var revokeRsp = await hr.DeleteAsync($"/api/admin/reporting-lines/{absentApprover}/vikar");
        Assert.Equal(HttpStatusCode.OK, revokeRsp.StatusCode);

        await WaitForEventAsync("vikarId", vikarId.ToString(), timeout: TimeSpan.FromSeconds(15));
        await BackdateVikarEventAsync(vikarId, AtUtcMidnight(F.AddDays(-5))); // well inside the 30-day window

        using var doc = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        Assert.DoesNotContain(doc.RootElement.GetProperty("expiredDelegations").EnumerateArray(),
            i => i.GetProperty("vikarId").GetGuid() == vikarId);
    }

    /// <summary>
    /// HRP-014 pin: an approver whose expired delegation has since been RE-COVERED (a fresh, still
    /// active <c>manager_vikar</c> row for the SAME absent approver) is reported with
    /// <c>approverHasActiveCover = true</c>, and is EXCLUDED from
    /// <c>expiredWithoutActiveCoverCount</c> — the raw expiry count (a historical fact) must not be
    /// mistaken for "still uncovered". RED if the <c>cover</c> self-join
    /// (<c>manager_vikar.absent_approver_id = ev.absent_approver_id AND effective_to IS NULL</c>)
    /// were dropped, or if <c>ExpiredWithoutActiveCoverCount</c> counted this item anyway.
    /// </summary>
    [Fact]
    public async Task UncoveredApprovers_ExpiredDelegation_ReCovered_ApproverHasActiveCoverTrue_ExcludedFromUncoveredCount()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var absentApprover = NextId("recover_absent");
        var firstVikar = NextId("recover_vikar1");
        var secondVikar = NextId("recover_vikar2");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, absentApprover, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, firstVikar, OrgA);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, secondVikar, OrgA);

        var expiredVikarId = await SeedManagerVikarAsync(absentApprover, firstVikar, OrgA, untilDate: F.AddDays(-40));
        await RunDelegationExpirySweepAsync(host); // closes the FIRST row (effective_to set) — frees the partial-unique key
        await WaitForEventAsync("vikarId", expiredVikarId.ToString(), timeout: TimeSpan.FromSeconds(15));
        await BackdateVikarEventAsync(expiredVikarId, AtUtcMidnight(F.AddDays(-10)));

        // A FRESH delegation for the SAME absent approver, still active today — the re-cover.
        await SeedManagerVikarAsync(absentApprover, secondVikar, OrgA, untilDate: F.AddMonths(2));

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        var item = Assert.Single(doc.RootElement.GetProperty("expiredDelegations").EnumerateArray(),
            i => i.GetProperty("vikarId").GetGuid() == expiredVikarId);
        Assert.True(item.GetProperty("approverHasActiveCover").GetBoolean());

        var totalExpired = doc.RootElement.GetProperty("expiredDelegationCount").GetInt32();
        var stillUncovered = doc.RootElement.GetProperty("expiredWithoutActiveCoverCount").GetInt32();
        Assert.True(totalExpired >= 1);
        Assert.Equal(totalExpired - 1, stillUncovered); // this recovered item is excluded from the "actually uncovered" tally
    }

    // ════════════════════════════════════════════════════════════════════════
    // HRP-015 — GET /api/hr/follow-up/cannot-register
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// HRP-015 pins (4): a gap that CLOSED yesterday (a successor row now covers today) does not
    /// appear; a gap that OPENED today does, with <c>gapSince = F</c>; an employee with TWO closed
    /// gaps (no live row) appears ONCE, with <c>gapSince</c> the LATEST close; a leaver does not
    /// appear despite an open gap. RED conditions: the end-exclusive comparison
    /// (<c>effective_to &gt; @today</c>) flipped to inclusive would make the "closed yesterday" leg
    /// wrongly absent AND the "opens today" leg wrongly present; dropping <c>DISTINCT ON</c> would
    /// duplicate the two-gaps employee; dropping the employment-window term would let the leaver
    /// leak through.
    /// </summary>
    [Fact]
    public async Task CannotRegister_GapEndingYesterday_Absent_OpensToday_Present_TwoGaps_Once_Leaver_Absent()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        // (a) closed yesterday, a successor row covers today onward — no gap today.
        var closedYesterday = NextId("cr_closed_yst");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, closedYesterday, OrgA);
        await CloseAgreementCodeRowAsync(closedYesterday, F.AddDays(-1));
        await SeedAgreementCodeRowAsync(closedYesterday, F.AddDays(-1), null);

        // (b) closed exactly today, no successor — the gap opens today.
        var opensToday = NextId("cr_opens_today");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, opensToday, OrgA);
        await CloseAgreementCodeRowAsync(opensToday, F);

        // (c) two historical closed rows, no live row — one item, gapSince = the LATEST close.
        var twoGaps = NextId("cr_two_gaps");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, twoGaps, OrgA);
        await CloseAgreementCodeRowAsync(twoGaps, F.AddDays(-60));
        await SeedAgreementCodeRowAsync(twoGaps, F.AddDays(-60), F.AddDays(-30));

        // (d) a gap AND a leaver — the leaver exclusion must win regardless.
        var leaverWithGap = NextId("cr_leaver_gap");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, leaverWithGap, OrgA);
        await CloseAgreementCodeRowAsync(leaverWithGap, F.AddDays(-10));
        await MarkLeaverAsync(leaverWithGap, F.AddDays(-30));

        using var doc = JsonDocument.Parse(await Client(host, HrToken(OrgA)).GetStringAsync("/api/hr/follow-up/cannot-register"));
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();

        Assert.DoesNotContain(items, i => i.GetProperty("employeeId").GetString() == closedYesterday);

        var opensTodayItem = Assert.Single(items, i => i.GetProperty("employeeId").GetString() == opensToday);
        Assert.Equal(F, ReadDate(opensTodayItem.GetProperty("gapSince")));
        Assert.Equal(0, opensTodayItem.GetProperty("daysSinceGapStart").GetInt32());

        var twoGapsMatches = items.Where(i => i.GetProperty("employeeId").GetString() == twoGaps).ToList();
        var twoGapsItem = Assert.Single(twoGapsMatches);
        Assert.Equal(F.AddDays(-30), ReadDate(twoGapsItem.GetProperty("gapSince")));

        Assert.DoesNotContain(items, i => i.GetProperty("employeeId").GetString() == leaverWithGap);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ?summary=true — the count matches the full-mode list length; the list is omitted, not the
    // population (pinned on the two multi-list reads: past-deadline and uncovered-approvers).
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>RED if <c>summary=true</c> changed the COUNT (rather than only hiding the list), or
    /// if the list were returned as an EMPTY array instead of JSON <c>null</c> (a client cannot then
    /// distinguish "not requested" from "genuinely nothing").</summary>
    [Fact]
    public async Task Summary_PastDeadline_CountsMatchFullMode_ListsNull()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("summary_pd");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);

        var hr = Client(host, HrToken(OrgA));
        using var full = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/past-deadline"));
        var fullCount = full.RootElement.GetProperty("employeeLateCount").GetInt32();
        var fullListLength = full.RootElement.GetProperty("employeeLate").GetArrayLength();
        Assert.Equal(fullCount, fullListLength);
        Assert.True(fullCount >= 1); // the seeded employee's never-sent months

        using var summary = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/past-deadline?summary=true"));
        Assert.Equal(fullCount, summary.RootElement.GetProperty("employeeLateCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("employeeLate").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("approverLate").ValueKind);
    }

    /// <summary>The second multi-list read: orphan + expired-delegation counts survive
    /// <c>summary=true</c> unchanged while both lists become <c>null</c>.</summary>
    [Fact]
    public async Task Summary_UncoveredApprovers_CountsMatchFullMode_ListsNull()
    {
        using var host = _factory.WithFixedToday(F);
        using var client = host.CreateClient();

        var employeeId = NextId("summary_orphan");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);

        var hr = Client(host, HrToken(OrgA));
        using var full = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/uncovered-approvers"));
        var fullOrphanCount = full.RootElement.GetProperty("orphanCount").GetInt32();
        Assert.Equal(fullOrphanCount, full.RootElement.GetProperty("orphans").GetArrayLength());
        Assert.True(fullOrphanCount >= 1);

        using var summary = JsonDocument.Parse(await hr.GetStringAsync("/api/hr/follow-up/uncovered-approvers?summary=true"));
        Assert.Equal(fullOrphanCount, summary.RootElement.GetProperty("orphanCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("orphans").ValueKind);
        Assert.Equal(JsonValueKind.Null, summary.RootElement.GetProperty("expiredDelegations").ValueKind);
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
        employeeId: "hr_s140_appr_actor", name: "hr_s140_appr_actor", role: StatsTidRoles.LocalHR, agreementCode: "AC",
        orgId: orgId, scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });

    private static string GlobalAdminToken() => NewTokenService().GenerateToken(
        employeeId: "hr_s140_appr_admin", name: "hr_s140_appr_admin", role: StatsTidRoles.GlobalAdmin, agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    private static string NoContributingScopeHrToken() => NewTokenService().GenerateToken(
        employeeId: "hr_s140_appr_noscope", name: "hr_s140_appr_noscope", role: StatsTidRoles.LocalHR, agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, OrgA, "ORG_AND_DESCENDANTS") });

    // ─────────────────────────────── delegation-expiry sweep driver ───────────────────────────────

    /// <summary>Grabs the host's OWN running <see cref="DelegationExpiryService"/> — registered only
    /// as <see cref="IHostedService"/> via <c>AddHostedService&lt;T&gt;()</c>, so it cannot be
    /// resolved as itself — and calls its single-shot sweep method directly. This is the SAME
    /// singleton instance the host started (DI caches it), not a second one, so there is no
    /// competing sweeper against this container.</summary>
    private static async Task RunDelegationExpirySweepAsync(WebApplicationFactory<Program> host)
    {
        var sweeper = host.Services.GetServices<IHostedService>().OfType<DelegationExpiryService>().Single();
        await sweeper.CloseExpiredDelegationsAsync(CancellationToken.None);
    }

    // ─────────────────────────────── seed helpers ───────────────────────────────

    private static DateTimeOffset AtUtcMidnight(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateOnly ReadDate(JsonElement e) => DateOnly.Parse(e.GetString()!);

    private static (DateOnly Start, DateOnly End) MonthBounds(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }

    /// <summary>Bounds the (employee × month) enumeration's window to start at
    /// <paramref name="start"/>, so a test employee's default UNBOUNDED history does not also
    /// surface every earlier "never sent" month in the rolling 12-month floor as noise alongside
    /// the one month a fact is actually pinning.</summary>
    private async Task SetEmploymentStartDateAsync(string employeeId, DateOnly start) =>
        await ExecAsync("UPDATE users SET employment_start_date = @p1 WHERE user_id = @p0", employeeId, start);

    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate) =>
        await ExecAsync(
            "UPDATE users SET employment_end_date = @p1, is_active = FALSE, end_date_deactivated = TRUE, updated_at = NOW() WHERE user_id = @p0",
            employeeId, endDate);

    private async Task SeedApprovalPeriodAsync(
        string employeeId, string orgId, int year, int month, string status,
        DateOnly? employeeDeadline = null, DateOnly? managerDeadline = null)
    {
        var (start, end) = MonthBounds(year, month);
        await ExecAsync(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                 agreement_code, ok_version, employee_deadline, manager_deadline)
            VALUES (gen_random_uuid(), @p0, @p1, @p2, @p3, 'MONTHLY', @p4, 'AC', 'OK24', @p5, @p6)
            """, employeeId, orgId, start, end, status,
            (object?)employeeDeadline ?? DBNull.Value, (object?)managerDeadline ?? DBNull.Value);
    }

    private async Task SeedPayrollExportRecordAsync(string employeeId, int year, int month) =>
        await ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            VALUES (gen_random_uuid(), NULL, @p0, @p1, @p2, '[]'::jsonb, '[]'::jsonb, @p3)
            """, employeeId, year, month, $"h-{employeeId}-{year}-{month}");

    private async Task<Guid> SeedManagerVikarAsync(string absentApproverId, string vikarUserId, string organisationId, DateOnly untilDate)
    {
        var vikarId = Guid.NewGuid();
        await ExecAsync(
            """
            INSERT INTO manager_vikar
                (vikar_id, absent_approver_id, vikar_user_id, until_date, reason, organisation_id, version, created_by)
            VALUES (@p0, @p1, @p2, @p3, 'ANDET', @p4, 1, 'test-seed-hr')
            """, vikarId, absentApproverId, vikarUserId, untilDate, organisationId);
        return vikarId;
    }

    private async Task CloseAgreementCodeRowAsync(string employeeId, DateOnly effectiveTo) =>
        await ExecAsync(
            "UPDATE user_agreement_codes SET effective_to = @p1 WHERE user_id = @p0 AND effective_to IS NULL",
            employeeId, effectiveTo);

    private async Task SeedAgreementCodeRowAsync(string employeeId, DateOnly effectiveFrom, DateOnly? effectiveTo) =>
        await ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @p0, 'AC', @p1, @p2, 1)
            """, employeeId, effectiveFrom, (object?)effectiveTo ?? DBNull.Value);

    // ─────────────────────────────── event polling + backdate (real elapsed time — the HTTP-polling exception) ───

    /// <summary>
    /// Polls the canonical <c>events</c> table (real elapsed wall-clock time — the one place this
    /// suite's fixed business clock does not apply; it measures publisher latency, not a business
    /// date) for a <c>ManagerVikarEnded</c> event whose <c>data-&gt;&gt;jsonKey</c> equals
    /// <paramref name="jsonValue"/>.
    /// </summary>
    private async Task WaitForEventAsync(string jsonKey, string jsonValue, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountAsync(
                    $"SELECT COUNT(*) FROM events WHERE event_type = 'ManagerVikarEnded' AND data->>'{jsonKey}' = @p0",
                    jsonValue) >= 1)
                return;
            await Task.Delay(200);
        }
        Assert.Fail($"Expected a ManagerVikarEnded event with {jsonKey}='{jsonValue}' within {timeout.TotalSeconds}s; the outbox publisher may not have drained in time.");
    }

    /// <summary>
    /// Backdates a REALLY-emitted <c>ManagerVikarEnded</c> event's <c>occurred_at</c> — an UPDATE,
    /// never an INSERT. <c>DomainEventBase.OccurredAt</c> is stamped from the real wall clock at
    /// emission time (a separate code path from the injected business-date seam this suite fixes
    /// everywhere else), so exact day-count boundary pins need this deliberate, declared calibration.
    /// </summary>
    private async Task BackdateVikarEventAsync(Guid vikarId, DateTimeOffset occurredAt) =>
        await ExecAsync(
            "UPDATE events SET occurred_at = @p1 WHERE event_type = 'ManagerVikarEnded' AND (data->>'vikarId')::uuid = @p0",
            vikarId, occurredAt);

    // ─────────────────────────────── raw DB helpers ───────────────────────────────

    private async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // compile-time test constant; values bound via parameters below
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
#pragma warning disable CA2100 // compile-time test constant; values bound via parameters below
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }
}
