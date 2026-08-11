using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S128 / TASK-12803 — the approval-period lock on <c>POST /api/time-entries</c> (S127 FU-D1, owner
/// ruling R3). The direct time-entry API was the ONLY registration write with no approval-status
/// check: it wrote into EMPLOYEE_APPROVED/APPROVED months, so post-send figures a manager reviewed
/// could silently drift. The fix mirrors the Skema save byte-for-byte via the shared
/// <c>ApprovalPeriodSaveLock</c> predicate + 409 construction site.
///
/// <para>The matrix here is the PLAIN (non-concurrent) arm — one POST per seeded period status:
/// <list type="bullet">
///   <item>EMPLOYEE_APPROVED / APPROVED → 409 Conflict naming the status, and — the write-side
///   invariant — ZERO new <c>time_entries_projection</c> rows and ZERO new outbox events, counted
///   as TOTALS on the employee/stream, never filtered by type (the S127 F6 lesson: a by-type COUNT
///   filters out exactly the spurious event it should catch).</item>
///   <item>DRAFT / SUBMITTED / REJECTED / no-period-row → still 201 with exactly one projection row
///   and one outbox event. SUBMITTED is DELIBERATE: legacy SUBMITTED stays writable (S127 owner
///   ruling R6); only the two manager-visible/locked states refuse.</item>
/// </list>
/// The concurrency arm (send-wins-then-POST, the race the in-transaction read exists for) lives
/// beside AC-7e in <c>SendConcurrencyTests</c>.</para>
///
/// <para>Every observation reads state back with no copy of the rule under test; isolation between
/// cases is by case-unique employee identity (the fixture's discipline).</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class TimeEntryPeriodLockTests : SendCommandMatrixTestBase
{
    public TimeEntryPeriodLockTests(SendCommandMatrixFixture fx) : base(fx) { }

    /// <summary>2026-03-10, a Tuesday inside the March period every case seeds.</summary>
    private static readonly DateOnly EntryDay = new(2026, 3, 10);

    private static string UniqueEmp(string tag) => $"s128te_{tag}_{Guid.NewGuid():N}"[..18];

    /// <summary>The direct time-entry POST (the defect's route), self-registered by the employee.</summary>
    private static Task<HttpResponseMessage> PostTimeEntryAsync(
        HttpClient client, string employeeId, DateOnly date, decimal hours = 7.4m)
        => client.PostAsJsonAsync("/api/time-entries",
            new { employeeId, date, hours, taskId = (string?)null, activityType = "NORMAL", agreementCode = "HK" });

    /// <summary>TOTAL projection rows for the employee — deliberately unfiltered (no date, no type).</summary>
    private Task<long> CountTimeEntryRowsTotalAsync(string employeeId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", employeeId));

    /// <summary>TOTAL outbox events on the employee's consolidated stream — ANY event type.</summary>
    private Task<long> CountEmployeeStreamOutboxTotalAsync(string employeeId)
        => ScalarLongAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id=@s", ("s", $"employee-{employeeId}"));

    // ── The locked statuses → 409, zero writes ────────────────────────────────────────────────────

    [Theory]
    [InlineData("EMPLOYEE_APPROVED")]
    [InlineData("APPROVED")]
    public async Task TimeEntryPost_IntoLockedMonth_Is409_AndWritesNothing(string lockedStatus)
    {
        var emp = UniqueEmp(lockedStatus is "APPROVED" ? "apr" : "eap");
        await SeedEmployeeAsync(emp);
        await SeedApprovalRowAsync(emp, lockedStatus, MarchStart, MarchEnd);

        // Snapshot BEFORE (both are 0 for a fresh employee, but delta-form keeps the assertion
        // honest against any future shared-fixture seeding).
        var rowsBefore = await CountTimeEntryRowsTotalAsync(emp);
        var outboxBefore = await CountEmployeeStreamOutboxTotalAsync(emp);

        var rsp = await PostTimeEntryAsync(EmployeeClient(emp), emp, EntryDay);

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Conflict, $"expected 409, got {(int)rsp.StatusCode}: {raw}");
        // The SHARED construction site's exact message — byte-identical to the Skema save's 409, by
        // construction (ApprovalPeriodSaveLock.PeriodLockedForSaveConflict).
        Assert.Contains($"Cannot save entries for a period with status {lockedStatus}", raw);

        // THE invariant: the refused POST left no trace — TOTAL counts, not filtered by type/date.
        Assert.Equal(rowsBefore, await CountTimeEntryRowsTotalAsync(emp));
        Assert.Equal(outboxBefore, await CountEmployeeStreamOutboxTotalAsync(emp));
    }

    // ── The writable statuses → still 201, exactly one row + one event ───────────────────────────

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("SUBMITTED")] // legacy SUBMITTED stays writable — S127 owner ruling R6, carried by R3
    [InlineData("REJECTED")]
    public async Task TimeEntryPost_IntoWritableStatus_IsStill201(string writableStatus)
    {
        var emp = UniqueEmp(writableStatus[..3].ToLowerInvariant());
        await SeedEmployeeAsync(emp);
        await SeedApprovalRowAsync(emp, writableStatus, MarchStart, MarchEnd);

        var rsp = await PostTimeEntryAsync(EmployeeClient(emp), emp, EntryDay);

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Created, $"expected 201, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal(1L, await CountTimeEntryRowsTotalAsync(emp));
        Assert.Equal(1L, await CountEmployeeStreamOutboxTotalAsync(emp));
    }

    /// <summary>No approval_periods row at all — nothing was ever sent, so the month is writable.
    /// (Known residual, carried per SPRINT-128 TASK-12803: the probe matches whole-calendar-month
    /// bounds exactly, so "no row ⇒ allow" is as weak here as on the Skema save — shared,
    /// pre-existing, out of this task's scope.)</summary>
    [Fact]
    public async Task TimeEntryPost_NoPeriodRow_IsStill201()
    {
        var emp = UniqueEmp("none");
        await SeedEmployeeAsync(emp);

        var rsp = await PostTimeEntryAsync(EmployeeClient(emp), emp, EntryDay);

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Created, $"expected 201, got {(int)rsp.StatusCode}: {raw}");
        Assert.Equal(1L, await CountTimeEntryRowsTotalAsync(emp));
        Assert.Equal(1L, await CountEmployeeStreamOutboxTotalAsync(emp));
    }
}
