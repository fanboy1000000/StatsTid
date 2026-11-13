using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S148 / TASK-14802-T fix-up (Step 5a W-2) — the month-lock status matrix on the Skema save, the one writer left after
/// <c>POST /api/time-entries</c> was retired (its own five-status matrix, <c>TimeEntryPeriodLockTests</c>, went with it).
/// <c>AC7d_SendVsSkemaSave_…</c> covers only EMPLOYEE_APPROVED, and under a race.
///
/// <para><b>Kind R (regression pin, holds today).</b> A save into a month whose <c>approval_periods</c> row is
/// EMPLOYEE_APPROVED or APPROVED answers 409 naming the status and writes nothing (zero time-entry rows, zero outbox
/// events on the employee's stream, counted as totals). DRAFT, SUBMITTED (legacy, stays writable), REJECTED, and a month
/// with no row at all answer 200 with one row and one outbox event.</para>
///
/// <para><b>Red conditions.</b> <c>ApprovalPeriodSaveLock.IsPeriodLockedForSave</c> widened (a writable status locked) fails
/// the 200 rows; narrowed (a locked status writable) fails the 409 rows. <b>Docker</b> — CI-pending.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class SkemaSaveMonthLockMatrixTests : SendCommandMatrixTestBase
{
    public SkemaSaveMonthLockMatrixTests(SendCommandMatrixFixture fx) : base(fx) { }

    /// <summary>2026-03-10, a Tuesday inside the March period every case seeds.</summary>
    private static readonly DateOnly EntryDay = new(2026, 3, 10);

    [Theory]
    [InlineData("EMPLOYEE_APPROVED", true)]
    [InlineData("APPROVED", true)]
    [InlineData("DRAFT", false)]
    [InlineData("SUBMITTED", false)]
    [InlineData("REJECTED", false)]
    [InlineData("NONE", false)]
    public async Task SkemaSave_ByMonthStatus_LockedIs409NothingWritten_OthersAre200(string status, bool locked)
    {
        var emp = $"s148lk_{status[..3].ToLowerInvariant()}_{Guid.NewGuid():N}"[..18];
        await SeedEmployeeAsync(emp);
        if (status != "NONE")
            await SeedApprovalRowAsync(emp, status, MarchStart, MarchEnd);

        var rsp = await EmployeeClient(emp).PostAsJsonAsync($"/api/skema/{emp}/save", new
        {
            year = MarchYear, month = MarchMonth,
            entries = new[] { new { date = EntryDay, projectCode = "PROJ-S148-LK", hours = 7.4m } },
        });
        var raw = await rsp.Content.ReadAsStringAsync();

        if (locked)
        {
            Assert.True(rsp.StatusCode == HttpStatusCode.Conflict, $"expected 409, got {(int)rsp.StatusCode}: {raw}");
            Assert.Contains($"Cannot save entries for a period with status {status}", raw);
            Assert.Equal(0L, await ScalarLongAsync(
                "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", emp)));
            Assert.Equal(0L, await CountOutboxTotalAsync($"employee-{emp}"));
        }
        else
        {
            Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"expected 200, got {(int)rsp.StatusCode}: {raw}");
            Assert.Equal(1L, await ScalarLongAsync(
                "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", emp)));
            Assert.Equal(1L, await CountOutboxTotalAsync($"employee-{emp}"));
        }
    }
}
