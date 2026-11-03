using System.Net;
using StatsTid.SharedKernel.Security;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T (AC-230-6, the payroll half; owner ruling OQ-2; refinement R-15) — the floors and the month bound on payroll's
/// <c>/calculate-and-export</c> for a switched-off leaver.
///
/// <para><b>Plain language.</b> From day 6 payroll's LocalAdmin (and above) may pay a switched-off leaver's final month, and nobody
/// else may. The same two promises as on the approval screens: (1) whoever is refused is refused identically for every month, so the refusal
/// cannot be used to find out when the leaver was employed; (2) a LocalAdmin who IS admitted, asking about a month in which the leaver was
/// not employed at all, gets a refusal with no date in it (payroll's copy of the body adds <c>success:false</c>), the same bytes for a month
/// before the start and a month after the end, and nothing is calculated, written or exported.</para>
///
/// <para><b>Where state comes from.</b> The leaver is switched off directly (<c>is_active = FALSE</c>); the Payroll host has no leaver job.
/// Docker-gated: <b>CI-pending</b>, never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("LeaverHrOnly")]
public sealed class LeaverHrOnlyPayrollFloorTests : LeaverHrOnlyTestBase
{
    public LeaverHrOnlyPayrollFloorTests(LeaverHrOnlyFixture fx) : base(fx) { }

    private static (string Token, string? Reason) ActorFor(string kind) => kind switch
    {
        // LocalAdmin over a DISJOINT organisation, but only LocalHR over the leaver's: primary role passes the route's policy, the covering scope
        // is below the LocalAdmin floor (S145 W-1, R9f1).
        "mixed_admin_hr" => (
            Token(NewId("pf_mix"), StatsTidRoles.LocalAdmin, OtherOrg,
                new RoleScope(StatsTidRoles.LocalAdmin, OtherOrg, "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalHR, HkOrg, "ORG_ONLY")),
            ScopeNotCoveringReason),
        // Below the route's LocalAdminOrAbove policy: refused by the policy, before any handler code.
        "local_hr" => (RoleToken(NewId("pf_hr"), StatsTidRoles.LocalHR), null),
        "leader" => (RoleToken(NewId("pf_lead"), StatsTidRoles.LocalLeader), null),
        _ => (RoleToken(NewId("pf_emp"), StatsTidRoles.Employee), null),
    };

    /// <summary>
    /// AC-230-6 (payroll). Kind <b>B</b> on the status (boundary pin, holds today: every caller is refused today) and <b>F</b> on the mixed-role
    /// leg's reason. From D+6 a switched-off leaver's month is refused 403 to a mixed-role token (LocalAdmin elsewhere, only LocalHR over the
    /// leaver), to a LocalHR, a LocalLeader and an Employee, and the refusal is the SAME bytes for March (employed) and April (not employed);
    /// the mixed-role leg answers "Actor scope does not cover target organization" (today "Target employee not found"). Nothing is calculated
    /// (the Rule Engine stand-in is never called for the leaver), written or exported.
    ///
    /// <para><b>Red conditions (argued).</b> The mixed-role reason assertion fails on today's code. Mutation M-order (the bound placed before the
    /// scope check) answers the mixed-role token 422 for April: the status and equal-bodies assertions fail. Mutation M-floor (the scope check
    /// without the LocalAdmin floor) admits the LocalHR scope: the status assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task FromDay6_PayrollRefusesNonAdmins_TheSameForEveryMonth_NothingCalculated()
    {
        var leaver = await SeedLeaverAsync(Cs, "pf_floor", start: new DateOnly(2026, 2, 2), end: LastDay);
        await PrepareExportableMarchAsync(leaver, marchClosing: 2.5m);
        await SeedPeriodAsync(Cs, leaver, "APPROVED", month: 3, approvedBy: "pf_hr_seed");

        foreach (var kind in new[] { "mixed_admin_hr", "local_hr", "leader", "employee" })
        {
            var (token, expectedReason) = ActorFor(kind);
            var raws = new List<string>();
            foreach (var month in new[] { 3, 4 })
            {
                var (status, body) = await ExportAsync(token, leaver, month);
                Assert.True(status == HttpStatusCode.Forbidden, $"{kind}, month {month}: expected 403, got {(int)status}: {body}");
                Assert.DoesNotContain("month_outside_employment_period", body);
                if (expectedReason is not null)
                    Assert.Contains($"\"reason\":\"{expectedReason}\"", body);
                raws.Add(body);
            }
            Assert.Equal(raws[0], raws[1]);
        }

        Assert.Equal(0, EngineCalls(Fx.Engine, leaver));
        Assert.Null(await ReadExportRecordAsync(Cs, leaver, 3));
        Assert.Null(await ReadExportRecordAsync(Cs, leaver, 4));
    }

    /// <summary>
    /// AC-230-6 (payroll). Kind <b>F</b>. An admitted LocalAdmin (in scope over the leaver's organisation) asking to export January (before the
    /// leaver's start, 2 February) or April (after her end, 13 March) gets the 422 EXACTLY as ruled plus <c>success:false</c> (payroll's
    /// copy), with no digit in the body and the SAME bytes for both months; nothing is calculated (the stand-in is never called), no export record
    /// or manifest is written. The same LocalAdmin exporting the employed March (approved, February recorded) gets 200 and the record, so the 422
    /// is about the month and not a blanket.
    ///
    /// <para><b>Red conditions.</b> Today payroll answers 403 ("Target employee not found") for a switched-off employee: the 422 assertions and
    /// the March 200 fail. Mutation M-bound (argued, Docker-only): dropping payroll's bound answers 403 "Period must be approved" (no approval row
    /// exists for January or April) instead of 422.</para>
    /// </summary>
    [Fact]
    public async Task AdmittedLocalAdmin_MonthWithNoEmployedDay_GetsTheDateFree422_WithSuccessFalse_NothingCalculated_March200()
    {
        var leaver = await SeedLeaverAsync(Cs, "pf_bound", start: new DateOnly(2026, 2, 2), end: LastDay);
        await PrepareExportableMarchAsync(leaver, marchClosing: 2.5m);
        await SeedPeriodAsync(Cs, leaver, "APPROVED", month: 3, approvedBy: "pf_hr_seed");
        var admin = LocalAdminToken(NewId("pf_admin"));

        var january = await AssertDateFreeRefusalAsync(await ExportRawAsync(admin, leaver, 1), payroll: true);
        var april = await AssertDateFreeRefusalAsync(await ExportRawAsync(admin, leaver, 4), payroll: true);
        Assert.Equal(january, april); // before-start and after-end are indistinguishable (ADR-040 D3)

        Assert.Equal(0, EngineCalls(Fx.Engine, leaver));
        Assert.Null(await ReadExportRecordAsync(Cs, leaver, 1));
        Assert.Null(await ReadExportRecordAsync(Cs, leaver, 4));
        Assert.Equal(0L, await ScalarAsync(Cs, "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", ("e", leaver)));

        var (status, body) = await ExportAsync(admin, leaver, 3);
        Assert.True(status == HttpStatusCode.OK, $"the employed March must export, got {(int)status}: {body}");
        Assert.NotNull(await ReadExportRecordAsync(Cs, leaver, 3));
    }

    /// <summary>The export call as an <see cref="HttpResponseMessage"/>, so the shared exact-body assertion can read it.</summary>
    private async Task<HttpResponseMessage> ExportRawAsync(string token, string employeeId, int month)
    {
        var (start, end) = Month(month);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                profile = Profile(employeeId),
                entries = Array.Empty<StatsTid.SharedKernel.Models.TimeEntry>(),
                absences = Array.Empty<StatsTid.SharedKernel.Models.AbsenceEntry>(),
                periodStart = start,
                periodEnd = end,
            }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await Fx.PayrollHost.CreateClient().SendAsync(request);
    }
}
