using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S138 / TASK-13802 (ADR-040 D8 as amended 2026-09-02) — the BACKDATING pins for
/// <c>PUT /api/admin/employee-profiles/{employeeId}</c>.
///
/// <para>
/// <b>What this suite is about, in plain language.</b> Until S138 an employee's profile history
/// could only be extended at the end: "her part-time fraction changed — as of today". A correction
/// ("no, it actually changed on the 10th") was unrecordable. This suite pins the corrected
/// behaviour end-to-end at the HTTP surface:
/// </para>
/// <list type="bullet">
///   <item><b>The split.</b> A backdate INTO a closed history row closes that row on the date and
///     inserts a new row running to where the closed row used to end; the rows AFTER it are
///     untouched, and the as-of resolver returns the right profile on both sides of the seam.</item>
///   <item><b>The refusals.</b> A date in the FUTURE and a date BEFORE the employment start are
///     both 422, and both bodies are DATE-FREE (the hire date is HR-scoped and must never reach
///     the wire; keeping both refusals on one shape also means a client cannot tell them apart
///     by probing).</item>
///   <item><b>The no-op vs the real write.</b> A request equal to the row COVERING the date writes
///     nothing and leaves the ETag alone. A backdated request equal to TODAY's values but
///     different from the covering row IS a change and writes — the exact silent-no-op this task
///     closes (refinement recon discovery 8).</item>
///   <item><b>The blast radius.</b> The absence revaluation covers exactly the written row's
///     interval (an absence belonging to the SUCCESSOR row keeps its feriedage) and SKIPS a
///     holiday year that has already been SETTLED, which instead surfaces as a SETTLED_YEAR row on
///     the HR worklist (owner ruling OQ-2 (i) — record the truth, never silently rewrite a frozen
///     ADR-033 disposition).</item>
///   <item><b>When that flag appears — S138 / TASK-13810 (owner ruling 2026-09-03).</b> A row is
///     raised when the correction both reaches the settlement's VALUATION BOUNDARY (the last day it counted) and
///     overlaps that year's entitlement window, and whenever the revaluation ACTUALLY skipped a
///     group (that second path takes no date test). An ordinary today-dated edit that touches
///     no settled group raises nothing (the removed false positive); a today-dated edit whose
///     revaluation does skip one still raises a row (the withheld correction is never silent); a
///     genuine backdate hit by both paths yields ONE row with TWO triggers.</item>
///   <item><b>What the PUT answers with — S138 / TASK-13810.</b> The 200 body is the state AS OF
///     TODAY on the real-write and the no-op branch alike, matching the dedicated agreement-code
///     endpoint. After a backdated correction it therefore does NOT echo what the caller sent —
///     intended, and pinned in both directions.</item>
///   <item><b>The hand-off to payroll.</b> Every already-EXPORTED month the interval touches lands
///     on the HR worklist with the right trigger kind (ADR-013: nothing recalculates by itself).</item>
///   <item><b>The fourth field + the token.</b> A category change raises its own trigger, refreshes
///     the live <c>users</c> cache only when the written row covers today, writes a
///     <c>users_audit</c> row for that bump — so a stale users ETag correctly 412s afterwards.</item>
///   <item><b>Concurrency + leavers.</b> Two backdates issued against the same profile ETag
///     serialize and the second 412s; HR can correct a DEACTIVATED employee's profile (their final
///     months are exactly the ones payroll still needs right).</item>
/// </list>
///
/// <para>
/// <b>RED-FIRST.</b> Every expected row set below was derived from the S138 refinement spec BEFORE
/// observing the implementation. Docker is unavailable on the authoring machine, so these facts
/// are compiler-validated locally and verify in CI (the standing S138 posture, refinement
/// Assumption 8).
/// </para>
///
/// <para>Conventions mirror <see cref="EmployeeProfileLifecycleTests"/> and
/// <see cref="Adr032RevaluationTests"/>: a per-test container, the WAF host, a GlobalAdmin token,
/// direct DB seeding for anything that predates "today".</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileBackdatingEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    /// <summary>A NON-default category (the users DEFAULT is 'Standard') so a path that forgot to
    /// write the field could never pass by coincidence of defaults.</summary>
    private const string NonDefaultCategory = "Fuldmægtig";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ═════════════════════════════════════════════════════════════════════
    // 1. The split — a backdate INTO a closed history row
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Timeline: [T-300, T-200) @ 1.000 · [T-200, T-100) @ 0.800 · [T-100, ∞) @ 0.600 (open).
    /// A PUT dated T-150 (interior to the MIDDLE, closed row) must:
    ///   • close the middle row at T-150 (it keeps 0.800 over [T-200, T-150));
    ///   • insert a new row [T-150, T-100) carrying the requested 0.500;
    ///   • leave the open row's interval AND values untouched;
    ///   • emit ONE EmployeeProfileSuperseded carrying newEffectiveTo = T-100 (the marker that
    ///     tells an insert-between apart from the classic cross-day supersession, where the new
    ///     row is open and newEffectiveTo is null);
    ///   • write ONE employee_profile_audit row with action='SUPERSEDED' whose previous_data is the
    ///     COVERING row's pre-image (fraction 0.800), NOT the open row's 0.600;
    ///   • bump the aggregate token: the profile GET's ETag moves even though the OPEN row's
    ///     content did not change (every timeline write bumps it, so a second backdate against the
    ///     stale ETag 412s).
    /// </summary>
    [Fact]
    public async Task PUT_BackdateIntoClosedHistoryRow_SplitsCoveringRow_LaterRowsUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        var t300 = Today.AddDays(-300);
        var t200 = Today.AddDays(-200);
        var t150 = Today.AddDays(-150);
        var t100 = Today.AddDays(-100);
        await ReplaceProfileTimelineAsync(employeeId,
            (t300, t200, 1.000m, "Base"),
            (t200, t100, 0.800m, "Middle"),
            (t100, null, 0.600m, "Live"));

        var client = AdminClient();
        var etagBefore = await ReadProfileVersionAsync(client, employeeId);

        var rsp = await PutProfileAsync(client, employeeId, t150,
            partTimeFraction: 0.500m, position: "Corrected", employmentCategory: null,
            ifMatch: $"\"{etagBefore}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The four rows, in date order.
        var rows = await ReadProfileTimelineAsync(employeeId);
        Assert.Equal(4, rows.Count);
        Assert.Equal((t300, t200, 1.000m), (rows[0].From, rows[0].To, rows[0].Fraction));
        Assert.Equal((t200, t150, 0.800m), (rows[1].From, rows[1].To, rows[1].Fraction));
        Assert.Equal((t150, t100, 0.500m), (rows[2].From, rows[2].To, rows[2].Fraction));
        Assert.Equal((t100, (DateOnly?)null, 0.600m), (rows[3].From, rows[3].To, rows[3].Fraction));

        // The as-of resolver on both sides of the new seam, and after the successor's start.
        Assert.Equal(0.800m, await ResolveFractionAtAsync(employeeId, t150.AddDays(-1)));
        Assert.Equal(0.500m, await ResolveFractionAtAsync(employeeId, t150));
        Assert.Equal(0.500m, await ResolveFractionAtAsync(employeeId, t100.AddDays(-1)));
        Assert.Equal(0.600m, await ResolveFractionAtAsync(employeeId, t100));

        // The Superseded event with the insert-between marker.
        var payload = await ReadLatestEventPayloadAsync(
            $"employee-profile-{employeeId}", "EmployeeProfileSuperseded");
        Assert.NotNull(payload);
        using var doc = JsonDocument.Parse(payload!);
        Assert.Equal(t200.ToString("yyyy-MM-dd"), doc.RootElement.GetProperty("predecessorEffectiveFrom").GetString());
        Assert.Equal(t150.ToString("yyyy-MM-dd"), doc.RootElement.GetProperty("predecessorEffectiveTo").GetString());
        Assert.Equal(t150.ToString("yyyy-MM-dd"), doc.RootElement.GetProperty("newEffectiveFrom").GetString());
        Assert.Equal(t100.ToString("yyyy-MM-dd"), doc.RootElement.GetProperty("newEffectiveTo").GetString());

        // The audit row: SUPERSEDED, previous_data from the COVERING row (0.800), not the live 0.600.
        var (action, previousData) = await ReadLatestProfileAuditAsync(employeeId);
        Assert.Equal("SUPERSEDED", action);
        Assert.NotNull(previousData);
        using var prev = JsonDocument.Parse(previousData!);
        Assert.Equal(0.800m, prev.RootElement.GetProperty("partTimeFraction").GetDecimal());

        // The aggregate token moved even though the OPEN row's content did not.
        var etagAfter = await ReadProfileVersionAsync(client, employeeId);
        Assert.True(etagAfter > etagBefore,
            $"the profile ETag must move on every timeline write (was {etagBefore}, now {etagAfter}).");
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. The two refusals — both DATE-FREE
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// S138 Step-5a (Reviewer, absorbed) — router case E: the correction lands BEFORE every
    /// recorded profile row, so nothing covers or precedes the date and no row records which
    /// employment category held then. The write is REFUSED rather than guessed.
    ///
    /// <para>
    /// Why refusing is the right answer: the only other source is the employee's LIVE category,
    /// which since S138 means "as of today" — stamping that on a months-old row would MISLABEL
    /// history, exactly what retiring the read-side COALESCE was meant to prevent. Every other
    /// routed case can recover the value that held (the covering row, or the row immediately
    /// before it), so this is the one shape where the caller must say. The hire date is set far
    /// enough back that the employment-start floor cannot fire first — the two refusals overlap in
    /// practice, which is why they carry distinct reasons and distinct messages.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PUT_BackdateBeforeFirstRow_WithoutCategory_Returns422_AndWritesNothing()
    {
        var hireDate = Today.AddDays(-400);
        var employeeId = await SeedEmployeeAsync(employmentStartDate: hireDate);
        var firstRowStart = Today.AddDays(-100);
        await ReplaceProfileTimelineAsync(employeeId, (firstRowStart, null, 1.000m, null));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, firstRowStart.AddDays(-30),
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var raw = await rsp.Content.ReadAsStringAsync();
        // Date-free (ADR-040 D7) — and it must name ITS OWN reason, not the employment-start one.
        Assert.DoesNotContain(firstRowStart.ToString("yyyy-MM-dd"), raw, StringComparison.Ordinal);
        Assert.Contains("employment category", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("employment start", raw, StringComparison.OrdinalIgnoreCase);

        Assert.Single(await ReadProfileTimelineAsync(employeeId));
        Assert.Equal(version, await ReadProfileVersionAsync(client, employeeId));
    }

    /// <summary>
    /// The same case E, WITH the category supplied: the caller has stated what held, so the write
    /// proceeds and stamps that value on the inserted row. Together with the fact above this shows
    /// the refusal is about MISSING INFORMATION, not about case E being forbidden.
    /// </summary>
    [Fact]
    public async Task PUT_BackdateBeforeFirstRow_WithCategory_Writes_AndStampsTheSuppliedValue()
    {
        var hireDate = Today.AddDays(-400);
        var employeeId = await SeedEmployeeAsync(employmentStartDate: hireDate);
        var firstRowStart = Today.AddDays(-100);
        await ReplaceProfileTimelineAsync(employeeId, (firstRowStart, null, 1.000m, null));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var insertedAt = firstRowStart.AddDays(-30);
        var rsp = await PutProfileAsync(client, employeeId, insertedAt,
            partTimeFraction: 0.500m, position: null, employmentCategory: "Chefkonsulent",
            ifMatch: $"\"{version}\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var timeline = await ReadProfileTimelineAsync(employeeId);
        Assert.Equal(2, timeline.Count);
        // Case E closes the inserted row AT the first row's start, never over it.
        var inserted = timeline.Single(r => r.From == insertedAt);
        Assert.Equal(firstRowStart, inserted.To);
        Assert.Equal("Chefkonsulent", inserted.Category);
        var original = timeline.Single(r => r.From == firstRowStart);
        Assert.Null(original.To);
    }

    /// <summary>
    /// A correction dated BEFORE the employee's recorded employment start is refused with 422, and
    /// the body must not contain the hire date in any form — it is HR-scoped data (same handling
    /// class as the birth date) and a 422 is visible to whoever made the call.
    /// </summary>
    [Fact]
    public async Task PUT_BackdateBeforeEmploymentStart_Returns422_WithNoDateInTheBody()
    {
        var hireDate = Today.AddDays(-100);
        var employeeId = await SeedEmployeeAsync(employmentStartDate: hireDate);
        await ReplaceProfileTimelineAsync(employeeId, (hireDate, null, 1.000m, null));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, hireDate.AddDays(-1),
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(hireDate.ToString("yyyy-MM-dd"), raw, StringComparison.Ordinal);
        Assert.DoesNotContain("provided", raw, StringComparison.OrdinalIgnoreCase);

        // Nothing was written.
        Assert.Single(await ReadProfileTimelineAsync(employeeId));
        Assert.Equal(version, await ReadProfileVersionAsync(client, employeeId));
    }

    /// <summary>
    /// A FUTURE-dated correction is still 422 (future-dating is Increment 4 by owner ruling — the
    /// open-ended-row readers, including the login token's agreement code, would treat a
    /// not-yet-effective row as current). Same DATE-FREE body as the employment-start floor.
    /// </summary>
    [Fact]
    public async Task PUT_FutureDatedEffectiveFrom_Returns422_WithNoDateInTheBody()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (Today.AddDays(-10), null, 1.000m, null));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var tomorrow = Today.AddDays(1);
        var rsp = await PutProfileAsync(client, employeeId, tomorrow,
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.DoesNotContain(tomorrow.ToString("yyyy-MM-dd"), raw, StringComparison.Ordinal);
        Assert.Single(await ReadProfileTimelineAsync(employeeId));
    }

    /// <summary>
    /// The presence guard. <c>effectiveFrom</c> is a non-nullable date on the wire, so a request
    /// that OMITS it binds 0001-01-01. Before S138 the "must equal today" rule rejected that as a
    /// side effect; now that any past date is legal it would route as a correction covering ALL
    /// recorded history, which is never what an omitted field means. It must stay a 422 — and the
    /// timeline must be untouched.
    /// </summary>
    [Fact]
    public async Task PUT_MissingEffectiveFrom_Returns422_AndWritesNothing()
    {
        var employeeId = await SeedEmployeeAsync();
        var t30 = Today.AddDays(-30);
        await ReplaceProfileTimelineAsync(employeeId, (t30, null, 1.000m, null));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(new { partTimeFraction = 0.500m, position = (string?)null }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var rsp = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
        Assert.Single(await ReadProfileTimelineAsync(employeeId));
        Assert.Equal(version, await ReadProfileVersionAsync(client, employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. No-op vs real write — the comparison is against the COVERING row
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A PUT whose values equal the row COVERING the requested date writes nothing: no new row, no
    /// profile event, no audit row, and the ETag is unchanged — but still 200, because from the
    /// caller's point of view the requested state now holds (the S23 no-op shape).
    /// </summary>
    [Fact]
    public async Task PUT_ValuesEqualToTheCoveringRow_IsNoOp_NothingWrittenEtagUnchanged()
    {
        var employeeId = await SeedEmployeeAsync();
        var t60 = Today.AddDays(-60);
        var t30 = Today.AddDays(-30);
        await ReplaceProfileTimelineAsync(employeeId,
            (t60, t30, 0.800m, "Middle"),
            (t30, null, 0.600m, "Live"));

        var client = AdminClient();
        var versionBefore = await ReadProfileVersionAsync(client, employeeId);
        var eventsBefore = await CountEventsAsync($"employee-profile-{employeeId}");
        var auditBefore = await CountProfileAuditAsync(employeeId);

        // Exactly the covering row's values, at a date interior to it.
        var rsp = await PutProfileAsync(client, employeeId, t60.AddDays(10),
            partTimeFraction: 0.800m, position: "Middle", employmentCategory: null,
            ifMatch: $"\"{versionBefore}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal(2, (await ReadProfileTimelineAsync(employeeId)).Count);
        Assert.Equal(eventsBefore, await CountEventsAsync($"employee-profile-{employeeId}"));
        Assert.Equal(auditBefore, await CountProfileAuditAsync(employeeId));
        Assert.Equal(versionBefore, await ReadProfileVersionAsync(client, employeeId));
        Assert.Equal($"\"{versionBefore}\"", rsp.Headers.ETag?.ToString());
    }

    /// <summary>
    /// The inverse, and the reason the predicate had to move off the live row: a backdated request
    /// whose values equal TODAY's values but DIFFER from the row covering that date IS a real
    /// change to history and must write. Under the pre-S138 "compare against the live row" rule
    /// this silently did nothing.
    /// </summary>
    [Fact]
    public async Task PUT_BackdateEqualToTodaysValuesButDifferentFromTheCoveringRow_Writes()
    {
        var employeeId = await SeedEmployeeAsync();
        var t60 = Today.AddDays(-60);
        var t30 = Today.AddDays(-30);
        // The OPEN row says 0.600; the covering (middle) row says 0.800.
        await ReplaceProfileTimelineAsync(employeeId,
            (t60, t30, 0.800m, "Middle"),
            (t30, null, 0.600m, "Live"));

        var client = AdminClient();
        var versionBefore = await ReadProfileVersionAsync(client, employeeId);

        // Request 0.600/"Live" — equal to the LIVE row, different from the COVERING row.
        var rsp = await PutProfileAsync(client, employeeId, t60.AddDays(10),
            partTimeFraction: 0.600m, position: "Live", employmentCategory: null,
            ifMatch: $"\"{versionBefore}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadProfileTimelineAsync(employeeId);
        Assert.Equal(3, rows.Count);
        Assert.Equal((t60, t60.AddDays(10), 0.800m), (rows[0].From, rows[0].To, rows[0].Fraction));
        Assert.Equal((t60.AddDays(10), t30, 0.600m), (rows[1].From, rows[1].To, rows[1].Fraction));
        Assert.True(await ReadProfileVersionAsync(client, employeeId) > versionBefore);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4. The revaluation window + the settled-year skip
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The revaluation must cover EXACTLY the written row's interval. Two VACATION absences are
    /// seeded: one INSIDE the corrected interval and one AFTER the successor row's start. The
    /// backdate halves the fraction over the corrected interval only, so the inside absence's
    /// feriedage doubles and the outside one is untouched — it belongs to the successor row, whose
    /// values this correction never changed.
    /// </summary>
    [Fact]
    public async Task PUT_Backdate_RevaluesOnlyInsideTheWrittenInterval()
    {
        var employeeId = await SeedEmployeeAsync();
        var t60 = Today.AddDays(-60);
        var t30 = Today.AddDays(-30);
        await ReplaceProfileTimelineAsync(employeeId,
            (t60, t30, 1.000m, null),
            (t30, null, 1.000m, null));

        var inside = t60.AddDays(10);   // inside [t45, t30) after the split below
        var outside = t30.AddDays(5);   // covered by the successor row
        await SeedAbsenceAsync(employeeId, inside, "VACATION", hours: 7.4m, feriedage: 1.0m);
        await SeedAbsenceAsync(employeeId, outside, "VACATION", hours: 7.4m, feriedage: 1.0m);

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, t60.AddDays(5),
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Inside the corrected interval: 7.4h at a half-time norm of 3.7 ⇒ 2.0 feriedage.
        Assert.Equal(2.0m, await ReadFeriedageAsync(employeeId, inside));
        // After the successor row's start: untouched.
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, outside));
    }

    /// <summary>
    /// Owner ruling OQ-2 (i): a correction whose interval reaches an ALREADY-SETTLED holiday year
    /// records the history change but must NOT rewrite that year's consumption — the settlement is
    /// a frozen ADR-033 disposition. Pinned on SPECIAL_HOLIDAY specifically (its entitlement year
    /// is the taking-window accrual year, not a calendar year — S80/8001). Two halves:
    ///   • the settled year's absence keeps its recorded feriedage and its balance <c>used</c> is
    ///     unchanged (the SKIP);
    ///   • a SETTLED_YEAR worklist row appears for that (type, year), pointing HR at ADR-033's
    ///     reverse-then-re-settle path (the FLAG — the consequence is visible, not hidden).
    ///
    /// <para>
    /// S138 / TASK-13810 sharpens the second half: this is a GENUINE backdate (it starts before the
    /// settlement was frozen), so BOTH settled-year paths select the year — the date rule and the
    /// "the revaluation actually skipped this group" rule. They share the row key, so the result is
    /// ONE row carrying TWO triggers, not two rows for HR to work twice.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PUT_Backdate_SkipsSettledSpecialHolidayYear_AndRaisesSettledYearWorklistRow()
    {
        var employeeId = await SeedEmployeeAsync();
        var t60 = Today.AddDays(-60);
        await ReplaceProfileTimelineAsync(employeeId, (Today.AddDays(-400), null, 1.000m, null));

        var absenceDay = t60;
        await SeedAbsenceAsync(employeeId, absenceDay, "SPECIAL_HOLIDAY", hours: 7.4m, feriedage: 1.0m);

        // SPECIAL_HOLIDAY reset_month is 1 (seeded config); the resolver maps the taking window to
        // its accrual year. Settle THAT year so the revaluation must skip it. The settlement was
        // frozen 30 days ago; the correction below starts 65 days ago, i.e. BEFORE the freeze.
        var settledYear = EntitlementPeriodResolver
            .Resolve(EntitlementPeriodResolver.SpecialHolidayType, 1, absenceDay).EntitlementYear;
        await SeedBalanceAsync(employeeId, "SPECIAL_HOLIDAY", settledYear, totalQuota: 5m, used: 1.0m);
        await SeedActiveSettlementAsync(employeeId, "SPECIAL_HOLIDAY", settledYear, Today.AddDays(-30));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, t60.AddDays(-5),
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The SKIP: the settled year's consumption is untouched despite the fraction halving.
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, absenceDay));
        Assert.Equal(1.0m, await ReadBalanceUsedAsync(employeeId, "SPECIAL_HOLIDAY", settledYear));

        // The FLAG: ONE SETTLED_YEAR worklist row for that (type, year) — reached by both paths, so
        // it carries TWO PROFILE_CHANGE triggers and sits at version 2.
        var worklist = await ReadWorklistRowsAsync(employeeId, "SETTLED_YEAR");
        var row = Assert.Single(worklist);
        Assert.Equal("SPECIAL_HOLIDAY", row.EntitlementType);
        Assert.Equal(settledYear, row.EntitlementYear);
        Assert.Contains("PROFILE_CHANGE", row.TriggersJson, StringComparison.Ordinal);
        Assert.Equal(2, await ReadWorklistTriggerCountAsync(employeeId, "SETTLED_YEAR"));
    }

    /// <summary>
    /// S138 / TASK-13810 (owner ruling 2026-09-03) — THE FALSE POSITIVE, GONE. Plain language: an
    /// ordinary "her fraction changes today" edit used to raise a SETTLED_YEAR row for any settled
    /// holiday year whose taking window still reached today — and those windows commonly run months
    /// past today, so the ordinary case produced a row that said nothing. A change from today
    /// forward cannot alter what a settlement froze in the past, and nothing here is skipped either
    /// (the settled year's only absence lies BEFORE the corrected interval, so the revaluation never
    /// considers it). Expected: no SETTLED_YEAR rows at all.
    /// </summary>
    [Fact]
    public async Task PUT_TodayDatedEdit_TouchingNoSettledGroup_RaisesNoSettledYearRow()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (Today.AddDays(-400), null, 1.000m, null));

        // The settled year's absence is in the PAST — outside the [today, ∞) corrected interval.
        var pastAbsence = Today.AddDays(-60);
        await SeedAbsenceAsync(employeeId, pastAbsence, "SPECIAL_HOLIDAY", hours: 7.4m, feriedage: 1.0m);
        var settledYear = EntitlementPeriodResolver
            .Resolve(EntitlementPeriodResolver.SpecialHolidayType, 1, pastAbsence).EntitlementYear;
        await SeedActiveSettlementAsync(employeeId, "SPECIAL_HOLIDAY", settledYear, Today.AddDays(-30));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, Today,
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Empty(await ReadWorklistRowsAsync(employeeId, "SETTLED_YEAR"));
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, pastAbsence)); // untouched: outside the window
    }

    /// <summary>
    /// S138 / TASK-13810 — the RESIDUAL the date rule alone would have hidden, and the reason the
    /// ruling has a second half. Plain language: a change effective TODAY still runs forward
    /// forever, and absences can already be BOOKED into the future. If one of those future days
    /// belongs to a holiday year that is already settled, the revaluation refuses to re-record it —
    /// the system withholds a correction HR believes it made. Dates say nothing here (the
    /// settlement was frozen yesterday, the correction starts today), so only the "what actually
    /// happened" path can produce this row. Expected: the future absence keeps its recorded value
    /// AND a SETTLED_YEAR row exists saying so.
    /// </summary>
    [Fact]
    public async Task PUT_TodayDatedEdit_WhoseRevaluationSkipsASettledGroup_RaisesASettledYearRow()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (Today.AddDays(-400), null, 1.000m, null));

        // An absence already booked 30 days AHEAD — inside the corrected interval [today, ∞).
        var futureAbsence = Today.AddDays(30);
        await SeedAbsenceAsync(employeeId, futureAbsence, "SPECIAL_HOLIDAY", hours: 7.4m, feriedage: 1.0m);
        var settledYear = EntitlementPeriodResolver
            .Resolve(EntitlementPeriodResolver.SpecialHolidayType, 1, futureAbsence).EntitlementYear;
        // Frozen YESTERDAY. The date rule's geometry half HOLDS (the corrected interval runs from
        // today forward and the year's taking window contains the future absence), but its
        // freeze-moment half does not — the correction starts after the freeze — so the conjunction
        // raises nothing and only the skip path can produce the row below.
        await SeedActiveSettlementAsync(employeeId, "SPECIAL_HOLIDAY", settledYear, Today.AddDays(-1));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, Today,
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The correction WAS withheld — the future day keeps its recorded feriedage …
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, futureAbsence));
        // … and it is not silent: exactly ONE row, ONE trigger (the skip path alone).
        var row = Assert.Single(await ReadWorklistRowsAsync(employeeId, "SETTLED_YEAR"));
        Assert.Equal("SPECIAL_HOLIDAY", row.EntitlementType);
        Assert.Equal(settledYear, row.EntitlementYear);
        Assert.Contains("PROFILE_CHANGE", row.TriggersJson, StringComparison.Ordinal);
        Assert.Equal(1, await ReadWorklistTriggerCountAsync(employeeId, "SETTLED_YEAR"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4b. The response body — the state AS OF TODAY (S138 / TASK-13810)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// S138 / TASK-13810 (owner ruling 2026-09-03) — RED-on-old. The PUT used to echo the values it
    /// had just written; after a BACKDATED correction that meant answering with a value that is not
    /// true today. The resource means "the profile as it stands now" everywhere else (the GET, the
    /// <c>users</c> caches, the ETag), so the correction endpoints must answer the same question.
    /// Here the corrected row is closed history and today's row still says 0.600/"Live" — that is
    /// what comes back, while the timeline shows the backdated row really was written.
    /// </summary>
    [Fact]
    public async Task PUT_Backdate_ResponseCarriesTodaysValues_NotTheBackdatedOnes()
    {
        var employeeId = await SeedEmployeeAsync();
        var t60 = Today.AddDays(-60);
        var t30 = Today.AddDays(-30);
        await ReplaceProfileTimelineAsync(employeeId,
            (t60, t30, 0.800m, "Middle"),
            (t30, null, 0.600m, "Live"));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, t60.AddDays(10),
            partTimeFraction: 0.500m, position: "Corrected", employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.600m, body.GetProperty("partTimeFraction").GetDecimal());
        Assert.Equal("Live", body.GetProperty("position").GetString());
        Assert.True(body.GetProperty("isPartTime").GetBoolean()); // derived from TODAY's fraction
        Assert.Equal(employeeId, body.GetProperty("employeeId").GetString());

        // The correction really happened — it is simply not today's truth.
        Assert.Equal(0.500m, await ResolveFractionAtAsync(employeeId, t60.AddDays(10)));
        Assert.Equal(0.600m, await ResolveFractionAtAsync(employeeId, Today));
    }

    /// <summary>
    /// The unchanged case, pinned so the ruling cannot silently move it: a TODAY-dated edit — the
    /// only shape the frontend sends before Increment 4's date picker — writes the row covering
    /// today, so "the state as of today" and "what you just sent" are the same values.
    /// </summary>
    [Fact]
    public async Task PUT_TodayDatedEdit_ResponseEchoesWhatItWrote()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (Today.AddDays(-60), null, 1.000m, "Before"));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, Today,
            partTimeFraction: 0.500m, position: "After", employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.500m, body.GetProperty("partTimeFraction").GetDecimal());
        Assert.Equal("After", body.GetProperty("position").GetString());
        Assert.True(body.GetProperty("isPartTime").GetBoolean());
    }

    /// <summary>
    /// The no-op branch follows the same rule. A backdated request equal to the row COVERING that
    /// date writes nothing and leaves the ETag alone — but the values it matched are a CLOSED
    /// history row's, so echoing the request would still have reported a fraction that is not
    /// today's. The body is today's state; the token is unchanged.
    /// </summary>
    [Fact]
    public async Task PUT_BackdatedNoOp_ResponseCarriesTodaysState_EtagUnchanged()
    {
        var employeeId = await SeedEmployeeAsync();
        var t60 = Today.AddDays(-60);
        var t30 = Today.AddDays(-30);
        await ReplaceProfileTimelineAsync(employeeId,
            (t60, t30, 0.800m, "Middle"),
            (t30, null, 0.600m, "Live"));

        var client = AdminClient();
        var versionBefore = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, t60.AddDays(10),
            partTimeFraction: 0.800m, position: "Middle", employmentCategory: null,
            ifMatch: $"\"{versionBefore}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.600m, body.GetProperty("partTimeFraction").GetDecimal());
        Assert.Equal("Live", body.GetProperty("position").GetString());
        Assert.Equal($"\"{versionBefore}\"", rsp.Headers.ETag?.ToString());
        Assert.Equal(2, (await ReadProfileTimelineAsync(employeeId)).Count); // nothing written
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5. The payroll hand-off — exported months on the worklist
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A correction whose interval spans TWO already-exported months raises exactly TWO
    /// EXPORTED_MONTH worklist rows — one per export record, each carrying a PROFILE_CHANGE
    /// trigger. Nothing recalculates automatically (ADR-013); the list is the hand-off.
    /// </summary>
    [Fact]
    public async Task PUT_BackdateAcrossTwoExportedMonths_RaisesTwoExportedMonthWorklistRows()
    {
        var employeeId = await SeedEmployeeAsync();
        // Two whole months safely in the past, plus a later row that bounds the correction.
        var monthA = new DateOnly(Today.AddMonths(-4).Year, Today.AddMonths(-4).Month, 1);
        var monthB = monthA.AddMonths(1);
        var boundary = monthB.AddMonths(1);
        await ReplaceProfileTimelineAsync(employeeId,
            (monthA.AddMonths(-1), boundary, 1.000m, null),
            (boundary, null, 1.000m, null));

        await SeedExportRecordAsync(employeeId, monthA.Year, monthA.Month);
        await SeedExportRecordAsync(employeeId, monthB.Year, monthB.Month);

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, monthA,
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadWorklistRowsAsync(employeeId, "EXPORTED_MONTH");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Contains("PROFILE_CHANGE", r.TriggersJson, StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Year == monthA.Year && r.Month == monthA.Month);
        Assert.Contains(rows, r => r.Year == monthB.Year && r.Month == monthB.Month);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 6. The fourth field — employment_category
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A category change on a TODAY-covering write: its own EMPLOYMENT_CATEGORY_CHANGE trigger, the
    /// live <c>users.employment_category</c> cache refreshed (the write covers today, so "the
    /// category as of today" really did move), a <c>users_audit</c> row for the resulting
    /// <c>users.version</c> bump — and therefore a users ETag captured BEFORE the profile PUT is
    /// now stale and 412s. That last leg is the point: the admin user DTO exposes the category, so
    /// the users token MUST move when it changes.
    /// </summary>
    [Fact]
    public async Task PUT_CategoryChangeCoveringToday_RefreshesCache_WritesUsersAudit_StaleUsersIfMatch412s()
    {
        var employeeId = await SeedEmployeeAsync();
        var t30 = Today.AddDays(-30);
        var monthNow = new DateOnly(Today.Year, Today.Month, 1);
        await ReplaceProfileTimelineAsync(employeeId, (t30, null, 1.000m, null));
        await SeedExportRecordAsync(employeeId, monthNow.Year, monthNow.Month);

        var client = AdminClient();
        var staleUsersEtag = await ReadUsersVersionAsync(client, employeeId);
        var profileVersion = await ReadProfileVersionAsync(client, employeeId);

        var rsp = await PutProfileAsync(client, employeeId, Today,
            partTimeFraction: 1.000m, position: null, employmentCategory: NonDefaultCategory,
            ifMatch: $"\"{profileVersion}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The dated row carries it AND the live cache followed (the row covers today).
        Assert.Equal(NonDefaultCategory, await ReadDatedCategoryAtAsync(employeeId, Today));
        Assert.Equal(NonDefaultCategory, await ReadUsersCategoryAsync(employeeId));

        // The users token moved and the transition is audited.
        var usersVersionAfter = await ReadUsersVersionAsync(client, employeeId);
        Assert.True(usersVersionAfter > staleUsersEtag);
        Assert.Equal(1, await CountUsersAuditAsync(employeeId, "UPDATED"));

        // The category trigger reached the worklist for the exported month.
        var worklist = await ReadWorklistRowsAsync(employeeId, "EXPORTED_MONTH");
        Assert.Single(worklist);
        Assert.Contains("EMPLOYMENT_CATEGORY_CHANGE", worklist[0].TriggersJson, StringComparison.Ordinal);

        // A users PUT holding the PRE-change ETag must now 412.
        var putUser = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{employeeId}")
        {
            Content = JsonContent.Create(new { displayName = "Renamed", effectiveFrom = Today.ToString("yyyy-MM-dd") }),
        };
        putUser.Headers.TryAddWithoutValidation("If-Match", $"\"{staleUsersEtag}\"");
        var userRsp = await client.SendAsync(putUser);
        Assert.Equal(HttpStatusCode.PreconditionFailed, userRsp.StatusCode);
    }

    /// <summary>
    /// The mirror: a category correction confined to CLOSED history leaves the live
    /// <c>users.employment_category</c> cache alone — the cache means "as of today", and today's
    /// row was not the one corrected. The dated cell still changes (that is the correction).
    /// </summary>
    [Fact]
    public async Task PUT_CategoryChangeInClosedHistory_LeavesTheLiveCacheUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        var t60 = Today.AddDays(-60);
        var t30 = Today.AddDays(-30);
        await ReplaceProfileTimelineAsync(employeeId,
            (t60, t30, 1.000m, null),
            (t30, null, 1.000m, null));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, t60.AddDays(5),
            partTimeFraction: 1.000m, position: null, employmentCategory: NonDefaultCategory,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal(NonDefaultCategory, await ReadDatedCategoryAtAsync(employeeId, t60.AddDays(5)));
        Assert.Equal("Standard", await ReadUsersCategoryAsync(employeeId));
        Assert.Equal(0, await CountUsersAuditAsync(employeeId, "UPDATED"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7. Concurrency + leavers
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two backdates issued against the SAME profile ETag: the first wins, the second 412s. Every
    /// timeline write bumps the aggregate token, so two corrections to DIFFERENT history rows still
    /// serialize — they succeed only as sequential retries with a refreshed ETag.
    /// </summary>
    [Fact]
    public async Task PUT_TwoBackdatesAgainstTheSameEtag_SecondReturns412()
    {
        var employeeId = await SeedEmployeeAsync();
        var t90 = Today.AddDays(-90);
        var t30 = Today.AddDays(-30);
        await ReplaceProfileTimelineAsync(employeeId,
            (t90, t30, 0.800m, null),
            (t30, null, 0.600m, null));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);

        var first = await PutProfileAsync(client, employeeId, t90.AddDays(10),
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PutProfileAsync(client, employeeId, t90.AddDays(20),
            partTimeFraction: 0.400m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);

        // The retry with the refreshed token succeeds — "the second 412s" is about the STALE token,
        // not about the second correction being illegal.
        var refreshed = await ReadProfileVersionAsync(client, employeeId);
        var retry = await PutProfileAsync(client, employeeId, t90.AddDays(20),
            partTimeFraction: 0.400m, position: null, employmentCategory: null,
            ifMatch: $"\"{refreshed}\"");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    /// <summary>
    /// HR corrects a DEACTIVATED employee's profile — the common case, not an edge case: a
    /// departed employee's final months are exactly the ones payroll still has to get right. The
    /// PUT is on the terminated-inclusive scope path with a LocalHR floor; it has no
    /// <c>is_active</c> switch, so admitting the leaver cannot become a reactivation side-door.
    /// </summary>
    [Fact]
    public async Task PUT_DeactivatedEmployee_HrBackdate_Returns200_AndDoesNotReactivate()
    {
        var employeeId = await SeedEmployeeAsync(isActive: false);
        var t60 = Today.AddDays(-60);
        await ReplaceProfileTimelineAsync(employeeId, (t60, null, 1.000m, null));

        var client = AdminClient();
        var version = await ReadProfileVersionAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, t60.AddDays(10),
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal(0.500m, await ResolveFractionAtAsync(employeeId, t60.AddDays(10)));
        Assert.False(await ReadUserIsActiveAsync(employeeId), "the profile PUT must never reactivate a leaver.");
    }

    // ─── Seeding helpers ─────────────────────────────────────────────────

    private async Task<string> SeedEmployeeAsync(
        DateOnly? employmentStartDate = null, bool isActive = true)
    {
        var employeeId = "emp_s138_bd_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, employment_category,
                               employment_start_date, is_active)
            VALUES (@u, @u, 'dev-only', 'S138 Backdating Test User', NULL,
                    @org, 'AC', 'OK24', 'Standard', @start, @active)
            """, conn))
        {
            userCmd.Parameters.AddWithValue("u", employeeId);
            userCmd.Parameters.AddWithValue("org", OrgId);
            userCmd.Parameters.AddWithValue("start", (object?)employmentStartDate ?? DBNull.Value);
            userCmd.Parameters.AddWithValue("active", isActive);
            await userCmd.ExecuteNonQueryAsync();
        }
        await using (var agreementCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, 'AC', '0001-01-01', NULL, 1)
            """, conn))
        {
            agreementCmd.Parameters.AddWithValue("u", employeeId);
            await agreementCmd.ExecuteNonQueryAsync();
        }
        return employeeId;
    }

    /// <summary>Replaces the employee's whole profile timeline with the supplied rows (the seeder
    /// may have created one at app boot). Versions ascend so the OPEN row's version is the
    /// aggregate token the GET stamps.</summary>
    private async Task ReplaceProfileTimelineAsync(
        string employeeId, params (DateOnly From, DateOnly? To, decimal Fraction, string? Position)[] rows)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var del = new NpgsqlCommand(
            "DELETE FROM employee_profiles WHERE employee_id = @e", conn))
        {
            del.Parameters.AddWithValue("e", employeeId);
            await del.ExecuteNonQueryAsync();
        }
        var version = 1L;
        foreach (var row in rows)
        {
            await using var ins = new NpgsqlCommand(
                """
                INSERT INTO employee_profiles
                    (profile_id, employee_id, part_time_fraction, position, employment_category,
                     effective_from, effective_to, version)
                VALUES (gen_random_uuid(), @e, @f, @p, 'Standard', @from, @to, @v)
                """, conn);
            ins.Parameters.AddWithValue("e", employeeId);
            ins.Parameters.AddWithValue("f", row.Fraction);
            ins.Parameters.AddWithValue("p", (object?)row.Position ?? DBNull.Value);
            ins.Parameters.AddWithValue("from", row.From);
            ins.Parameters.AddWithValue("to", (object?)row.To ?? DBNull.Value);
            ins.Parameters.AddWithValue("v", version++);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedAbsenceAsync(
        string employeeId, DateOnly date, string absenceType, decimal hours, decimal feriedage)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absences_projection
                (event_id, employee_id, date, absence_type, hours, feriedage,
                 agreement_code, ok_version, occurred_at, actor_id, actor_role, outbox_id)
            VALUES (gen_random_uuid(), @e, @d, @t, @h, @f, 'AC', 'OK24', NOW(), 'test-seed', 'Employee', -1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        cmd.Parameters.AddWithValue("t", absenceType);
        cmd.Parameters.AddWithValue("h", hours);
        cmd.Parameters.AddWithValue("f", feriedage);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedBalanceAsync(
        string employeeId, string entitlementType, int year, decimal totalQuota, decimal used)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_balances
                (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used)
            VALUES (gen_random_uuid(), @e, @t, @y, @q, @u)
            ON CONFLICT (employee_id, entitlement_type, entitlement_year)
            DO UPDATE SET total_quota = EXCLUDED.total_quota, used = EXCLUDED.used
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("q", totalQuota);
        cmd.Parameters.AddWithValue("u", used);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// An ACTIVE (SETTLED, non-REVERSED) settlement row for the tuple — what makes the revaluation
    /// skip the year and the worklist flag it. <paramref name="boundaryDate"/> is the settlement's
    /// VALUATION BOUNDARY — the last day it counted — written into the immutable snapshot as
    /// <c>settlementBoundaryDate</c>: S138 / TASK-13810's date rule raises a SETTLED_YEAR row only
    /// when the correction starts BEFORE it, so every settled-year test states this date explicitly
    /// rather than inheriting a window.
    /// </summary>
    private async Task SeedActiveSettlementAsync(
        string employeeId, string entitlementType, int year, DateOnly boundaryDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot)
            VALUES (@e, @t, @y, 1, 'SETTLED', 'YEAR_END',
                    jsonb_build_object('settlementBoundaryDate', to_char(@b::date, 'YYYY-MM-DD')))
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("b", boundaryDate);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedExportRecordAsync(string employeeId, int year, int month)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month,
                 original_lines, current_effective_lines, content_hash)
            VALUES (gen_random_uuid(), NULL, @e, @y, @m, '[]'::jsonb, '[]'::jsonb, @hash)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("m", month);
        cmd.Parameters.AddWithValue("hash", $"hash-{year}-{month}");
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── Read helpers ────────────────────────────────────────────────────

    private sealed record ProfileRow(DateOnly From, DateOnly? To, decimal Fraction, string? Position, string? Category);

    private async Task<IReadOnlyList<ProfileRow>> ReadProfileTimelineAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_from, effective_to, part_time_fraction, position, employment_category
            FROM employee_profiles WHERE employee_id = @e ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var rows = new List<ProfileRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ProfileRow(
                reader.GetFieldValue<DateOnly>(0),
                reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }
        return rows;
    }

    /// <summary>The as-of resolution, read straight off dated history (end-exclusive) — the same
    /// predicate every resolver-fed reader uses.</summary>
    private async Task<decimal> ResolveFractionAtAsync(string employeeId, DateOnly asOf)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT part_time_fraction FROM employee_profiles
            WHERE employee_id = @e AND effective_from <= @d
              AND (effective_to IS NULL OR @d < effective_to)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", asOf);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, $"no profile row covers {asOf:yyyy-MM-dd}.");
        return (decimal)result!;
    }

    private async Task<string?> ReadDatedCategoryAtAsync(string employeeId, DateOnly asOf)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employment_category FROM employee_profiles
            WHERE employee_id = @e AND effective_from <= @d
              AND (effective_to IS NULL OR @d < effective_to)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", asOf);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<string?> ReadUsersCategoryAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT employment_category FROM users WHERE user_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<bool> ReadUserIsActiveAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT is_active FROM users WHERE user_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<decimal> ReadFeriedageAsync(string employeeId, DateOnly date)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT feriedage FROM absences_projection WHERE employee_id = @e AND date = @d", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("d", date);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, $"expected a recorded feriedage on {date:yyyy-MM-dd}.");
        return (decimal)result!;
    }

    private async Task<decimal> ReadBalanceUsedAsync(string employeeId, string entitlementType, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT used FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("y", year);
        var result = await cmd.ExecuteScalarAsync();
        Assert.False(result is null or DBNull, "expected an entitlement_balances row.");
        return (decimal)result!;
    }

    private sealed record WorklistRow(string Kind, int? Year, int? Month, string? EntitlementType, int? EntitlementYear, string TriggersJson);

    private async Task<IReadOnlyList<WorklistRow>> ReadWorklistRowsAsync(string employeeId, string kind)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT kind, year, month, entitlement_type, entitlement_year, triggers::text
            FROM hr_backdate_worklist
            WHERE employee_id = @e AND kind = @k
            ORDER BY created_at, worklist_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("k", kind);
        var rows = new List<WorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new WorklistRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetString(5)));
        }
        return rows;
    }

    /// <summary>The total number of trigger elements across the employee's OPEN rows of one kind —
    /// how the "one row, two triggers" collapse (S138 / TASK-13810) is told apart from "one row,
    /// one trigger".</summary>
    private async Task<int> ReadWorklistTriggerCountAsync(string employeeId, string kind)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(jsonb_array_length(triggers)), 0)
            FROM hr_backdate_worklist
            WHERE employee_id = @e AND kind = @k AND resolved_at IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("k", kind);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<string?> ReadLatestEventPayloadAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @s AND event_type = @t
            ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", streamId);
        cmd.Parameters.AddWithValue("t", eventType);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<long> CountEventsAsync(string streamId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", conn);
        cmd.Parameters.AddWithValue("s", streamId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountProfileAuditAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM employee_profile_audit WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountUsersAuditAsync(string userId, string action)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users_audit WHERE user_id = @u AND action = @a", conn);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("a", action);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<(string Action, string? PreviousData)> ReadLatestProfileAuditAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, previous_data::text FROM employee_profile_audit
            WHERE employee_id = @e ORDER BY audit_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "expected an employee_profile_audit row.");
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    // ─── HTTP helpers ────────────────────────────────────────────────────

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
        return client;
    }

    private static string MintGlobalAdminToken()
    {
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return svc.GenerateToken(
            employeeId: "ADMIN_S138_BD",
            name: "S138 Backdating Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private static async Task<long> ReadProfileVersionAsync(HttpClient client, string employeeId)
    {
        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("version").GetInt64();
    }

    private static async Task<long> ReadUsersVersionAsync(HttpClient client, string userId)
    {
        var rsp = await client.GetAsync($"/api/admin/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("version").GetInt64();
    }

    private static async Task<HttpResponseMessage> PutProfileAsync(
        HttpClient client, string employeeId, DateOnly effectiveFrom,
        decimal partTimeFraction, string? position, string? employmentCategory, string ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                partTimeFraction,
                position,
                employmentCategory,
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }
}
