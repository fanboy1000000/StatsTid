using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Approval;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S147 / TASK-14710-T (AC-230-5, the Employee bullet and the LocalHR self-only bullet; rulings R-21, R-22, R-24;
/// owner OQ-2; Orchestrator ruling of fix-up 1 on the refusal body) — "a leaver's own access ends the day after
/// their last working day, whatever their role", proven route by route over a SEEDED subject.
///
/// <para><b>Plain language.</b> Mette left on Friday 13 March 2026. Her account is still switched on (the
/// manager's five-day grace), and a login token issued on her last day is valid for eight hours with no way to
/// recall it. From day 1 that token must not change anything of her own: not her Skema, not her time entries,
/// not sending or approving her month, not reopening it, not her row preferences, not her overtime payout choice.
/// The answer is a 403 that says (without any date) "A departed employee cannot act on their own record". This
/// holds for an ordinary employee and — on the two self-only routes that no other check reaches — for an HR user
/// acting on her own record.</para>
///
/// <para><b>Refusal body.</b> Two sentences exist (Orchestrator ruling, fix-up 1): the departed-self clause answers
/// <see cref="DepartedSubject.DenyReason"/>; the role clause answers ADR-040 D3's "Writes for a deactivated
/// employee require LocalHR or above". Where both apply — an Employee acting on themselves — the self sentence
/// wins, and every fact below pins exactly that, and pins that the other sentence is NOT used.</para>
///
/// <para><b>Seeding.</b> The state is the real grace state: <c>is_active = TRUE</c> with a FIXED past end date
/// (Friday 2026-03-13), departed for any real "today" after it, so no fact depends on the calendar day CI runs.
/// The send-command fixture's host has no leaver job, so the active departed subject is not flipped under the test.
/// A "token issued at 23:00 on D" is any valid token: the floor reads the subject row, not the token's age.
/// <b>Docker</b> — CI-pending.</para>
///
/// <para><b>Kinds.</b> Each Employee fact is F: on today's code the floor tests only <c>!IsActive</c>, so the
/// active subject is admitted (200/201) on the Skema save, the time entry, send and employee-approve, and the
/// reopen, row-preferences and compensation-choice routes have no floor at all. The LocalHR fact is F for the
/// same reason on the two self-only routes. The wave-gate mutations: M-9a drops the identity arm of the floor
/// (turns the LocalHR fact RED); M-9c checks the role arm first (turns every body assertion RED).</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendCommandMatrix")]
public sealed class DepartedSelfServiceTests : SendCommandMatrixTestBase
{
    /// <summary>The leaver's last day employed: Friday 2026-03-13.</summary>
    private static readonly DateOnly LastDay = new(2026, 3, 13);

    public DepartedSelfServiceTests(SendCommandMatrixFixture fx) : base(fx) { }

    /// <summary>403 carrying exactly the departed-self sentence — not the role sentence, no date anywhere.</summary>
    private static async Task AssertSelfRefusalAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("A departed employee cannot act on their own record", doc.RootElement.GetProperty("reason").GetString());
        Assert.Equal(DepartedSubject.DenyReason, doc.RootElement.GetProperty("reason").GetString());
        Assert.DoesNotContain(DepartedSubjectWriteFloor.DenyReason, raw);   // the role sentence is not used for a self refusal
        Assert.DoesNotContain("2026", raw);                                  // date-free (ADR-040 D7)
    }

    private async Task<string> SeedMetteAsync(string id, string agreementCode = "HK")
    {
        await SeedEmployeeAsync(id, Org, agreementCode);
        await SetEmploymentWindowAsync(id, start: null, end: LastDay); // active, past her last day (the grace state)
        return id;
    }

    // ═════════════════════════════ Mette as an Employee ═════════════════════════════

    /// <summary>Kind F. Own Skema save: 403 with the self sentence, nothing written. Red condition: <c>Assert 403</c> —
    /// today the active subject passes <c>!IsActive</c> and the save answers 200.</summary>
    [Fact]
    public async Task Employee_OwnSkemaSave_403()
    {
        var mette = await SeedMetteAsync("ds14710_emp_skema");
        using var client = EmployeeClient(mette);

        var rsp = await client.PostAsJsonAsync($"/api/skema/{mette}/save", new
        {
            year = MarchYear, month = MarchMonth,
            entries = new[] { new { date = new DateOnly(2026, 3, 10), projectCode = "PROJ-S147", hours = 7.4m } },
        });

        await AssertSelfRefusalAsync(rsp);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", mette)));
        Assert.Equal(0L, await CountOutboxTotalAsync($"employee-{mette}"));
    }

    /// <summary>Kind F. Own time entry: 403 with the self sentence, no row. Red condition: <c>Assert 403</c> — today 201.</summary>
    [Fact]
    public async Task Employee_OwnTimeEntry_403()
    {
        var mette = await SeedMetteAsync("ds14710_emp_entry");
        using var client = EmployeeClient(mette);

        var rsp = await client.PostAsJsonAsync("/api/time-entries", new
        {
            employeeId = mette, date = new DateOnly(2026, 3, 10), hours = 7.4m,
            taskId = (string?)null, activityType = "NORMAL", agreementCode = "HK",
        });

        await AssertSelfRefusalAsync(rsp);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM time_entries_projection WHERE employee_id=@e", ("e", mette)));
        Assert.Equal(0L, await CountOutboxTotalAsync($"employee-{mette}"));
    }

    /// <summary>Kind F. Own send of March (a covered month, so no coverage 422 can mask the floor): 403 with the self
    /// sentence, no approval period created. Red condition: <c>Assert 403</c> — today 200 EMPLOYEE_APPROVED.</summary>
    [Fact]
    public async Task Employee_OwnSend_403()
    {
        var mette = await SeedMetteAsync("ds14710_emp_send");
        await CoverMonthWithAbsencesAsync(mette, gap: null, to: LastDay);
        using var client = EmployeeClient(mette);

        var rsp = await PostSendAsync(client, mette);

        await AssertSelfRefusalAsync(rsp);
        Assert.False(await RowExistsAsync(mette, MarchStart, MarchEnd), "a refused send must create no approval period");
        Assert.Equal(0L, await CountOutboxTotalAsync(StreamId(mette)));
    }

    /// <summary>Kind F. Own employee-approve (the by-id adapter, the same one send floor): 403 with the self sentence,
    /// the DRAFT period untouched. Red condition: <c>Assert 403</c> — today 200.</summary>
    [Fact]
    public async Task Employee_OwnEmployeeApprove_403()
    {
        var mette = await SeedMetteAsync("ds14710_emp_approve");
        await CoverMonthWithAbsencesAsync(mette, gap: null, to: LastDay);
        var periodId = await SeedApprovalRowAsync(mette, "DRAFT", MarchStart, MarchEnd);
        using var client = EmployeeClient(mette);

        var rsp = await PostEmployeeApproveAsync(client, periodId);

        await AssertSelfRefusalAsync(rsp);
        Assert.Equal("DRAFT", (await ReadRowAsync(periodId))!.Status);
        Assert.Equal(0L, await CountOutboxTotalAsync(StreamId(mette)));
    }

    /// <summary>Kind F. Own reopen through the Employee arm of her EMPLOYEE_APPROVED month: 403 with the self sentence,
    /// status unchanged. Red condition: <c>Assert 403</c> and the unchanged status — today the own-data branch of the
    /// validator reads no subject and the month is reopened to DRAFT (200).</summary>
    [Fact]
    public async Task Employee_OwnReopen_EmployeeArm_403()
    {
        var mette = await SeedMetteAsync("ds14710_emp_reopen");
        var periodId = await SeedApprovalRowAsync(mette, "EMPLOYEE_APPROVED", MarchStart, MarchEnd);
        using var client = EmployeeClient(mette);

        var rsp = await PostReopenAsync(client, periodId, "s147 pin: reopen own month");

        await AssertSelfRefusalAsync(rsp);
        Assert.Equal("EMPLOYEE_APPROVED", (await ReadRowAsync(periodId))!.Status);
        Assert.Equal(0L, await CountOutboxTotalAsync(StreamId(mette)));
    }

    /// <summary>Kind F. Own row-preferences PUT: 403 with the self sentence, nothing stored. Red condition:
    /// <c>Assert 403</c> — today the self-only route has no floor and answers 200.</summary>
    [Fact]
    public async Task Employee_OwnRowPreferencesPut_403()
    {
        var mette = await SeedMetteAsync("ds14710_emp_rowpref");
        using var client = EmployeeClient(mette);

        var rsp = await client.PutAsJsonAsync($"/api/skema/{mette}/row-preferences",
            new { projects = Array.Empty<object>(), absenceTypes = Array.Empty<object>() });

        await AssertSelfRefusalAsync(rsp);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM user_project_selections WHERE employee_id=@e", ("e", mette)));
    }

    /// <summary>Kind F. Own compensation-choice PUT: 403 with the self sentence, no balance row. Red condition:
    /// <c>Assert 403</c> — today the self-only route has no floor (it answers 200, or the config's 400, never 403).</summary>
    [Fact]
    public async Task Employee_OwnCompensationChoicePut_403()
    {
        var mette = await SeedMetteAsync("ds14710_emp_comp");
        using var client = EmployeeClient(mette);

        var rsp = await client.PutAsJsonAsync($"/api/overtime/{mette}/compensation-choice",
            new { periodYear = 2026, compensationModel = "UDBETALING" });

        await AssertSelfRefusalAsync(rsp);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM overtime_balances WHERE employee_id=@e", ("e", mette)));
    }

    // ═════════════════════════ Mette as LocalHR — the self-only routes ═════════════════════════

    /// <summary>
    /// Kind F — the discriminating fact for the floor's identity arm (M-9a). An HR user who has left cannot change
    /// her OWN row preferences or her OWN compensation choice: both routes are self-only and never reach the
    /// organisation-scope validator, so the floor is the only layer that can refuse an HR self. 403 with the self
    /// sentence on each, nothing written. (Her Skema save, time entry, send and leader-arm reopen are closed by the
    /// validator clause and pinned by TASK-14703-T.) Red conditions: <c>Assert 403</c> on each — today neither route
    /// has any floor, so both succeed whatever her role; with M-9a (drop the identity arm) both answer 200 again.
    /// </summary>
    [Fact]
    public async Task LocalHr_OwnRowPreferences_CompensationChoice_403()
    {
        var mette = await SeedMetteAsync("ds14710_hr_self");
        using var client = ClientForRole(mette, StatsTidRoles.LocalHR, Org);

        var rowPrefs = await client.PutAsJsonAsync($"/api/skema/{mette}/row-preferences",
            new { projects = Array.Empty<object>(), absenceTypes = Array.Empty<object>() });
        await AssertSelfRefusalAsync(rowPrefs);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM user_project_selections WHERE employee_id=@e", ("e", mette)));

        var comp = await client.PutAsJsonAsync($"/api/overtime/{mette}/compensation-choice",
            new { periodYear = 2026, compensationModel = "UDBETALING" });
        await AssertSelfRefusalAsync(comp);
        Assert.Equal(0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM overtime_balances WHERE employee_id=@e", ("e", mette)));
    }

    // ═════════════════════════════ the floor does not over-reach ═════════════════════════════

    /// <summary>
    /// Kind B — boundary pin, holds today (an employee with no end date saving their own row preferences and
    /// registering their own time is not a leaver). The new floor must never refuse a person who has not departed.
    /// Row-preferences 200, time entry 201.
    /// </summary>
    [Fact]
    public async Task Employee_NoEndDate_OwnWrites_StillAllowed()
    {
        const string emp = "ds14710_emp_open";
        await SeedEmployeeAsync(emp, Org, "HK");
        using var client = EmployeeClient(emp);

        var rowPrefs = await client.PutAsJsonAsync($"/api/skema/{emp}/row-preferences",
            new { projects = Array.Empty<object>(), absenceTypes = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, rowPrefs.StatusCode);

        var entry = await client.PostAsJsonAsync("/api/time-entries", new
        {
            employeeId = emp, date = new DateOnly(2026, 3, 10), hours = 7.4m,
            taskId = (string?)null, activityType = "NORMAL", agreementCode = "HK",
        });
        Assert.Equal(HttpStatusCode.Created, entry.StatusCode);
    }
}
