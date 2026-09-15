using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S141 / TASK-14106, wave 2 — the ENDPOINT-dependent profile-side pins for Increment 4's
/// "schedule a change ahead" feature (ADR-040, owner rulings OQ-3/OQ-5/OQ-6, requirement B0).
///
/// <para>
/// <b>Why these pins exist, in plain language.</b> Before this sprint, every write to an employee's
/// profile had to be dated today or earlier, so "the row with no end date" and "the row describing
/// today" were always the same row. HR can now schedule a change for a future date (e.g. "she goes
/// part-time from 1 November"), and that coincidence stops holding. This file pins the four defects
/// that coincidence was quietly hiding, all reachable only through the live HTTP endpoints:
/// </para>
/// <list type="bullet">
///   <item><b>B6 — the round-trip must be inert.</b> If HR opens the edit drawer, changes an
///     unrelated field and saves, nothing about the ALREADY-scheduled change may move and no
///     already-booked absence may be silently revalued.</item>
///   <item><b>B5 — the concurrency token.</b> Before this sprint the token was the open row's own
///     version, which was harmless only because the open row was the whole timeline. Now the token
///     is <c>users.version</c> (owner ruling OQ-3 (a)) and must work at all THREE sites: the GET's
///     ETag source, the PUT's validation, and the DELETE's validation.</item>
///   <item><b>B4 / OQ-5 (a) — deleting a profile while a change is scheduled.</b> The delete must
///     close today's row, retire every scheduled row (never leaving an inverted interval), record
///     TODAY's values (not the future row's) as the audit's <c>previous_data</c>, and audit the
///     retirement itself — a row that was audited into existence must not vanish unrecorded.</item>
///   <item><b>OQ-6 (a) — a today-dated edit while a change is scheduled.</b> HR must be asked
///     whether the edit applies only until the scheduled change, or should also be carried into it —
///     and "carried into it" must touch ONLY the field HR actually edited, never the rest of a
///     colleague's scheduled decision.</item>
///   <item><b>B0 — the read payload carries the scheduled change</b>, so no screen has to discover
///     it with a second call.</item>
///   <item><b>A future write leaves the <c>users.employment_category</c> cache untouched</b> until
///     the scheduled date actually arrives.</item>
///   <item><b>The revaluation + worklist rules</b> apply unchanged to a FUTURE-dated write, not only
///     to a backdate: absences dated on/after the write's effective date are revalued, absences
///     before it are untouched, the revaluation stops at the next already-scheduled row's start, and
///     the HR worklist keeps its existing (non-suppressed) exported-month behaviour.</item>
/// </list>
///
/// <para>
/// <b>RED-FIRST, and expected to STAY red until the S141 wave-2 gate.</b> These pins are written
/// against the SPEC (this sprint's refinement + owner rulings), not against the code, and they are
/// authored WHILE the endpoint-side work (TASK-14104: lifting the three endpoint future-date
/// refusals, wiring B0's read payload, the OQ-6 request field, the mandatory delete-retirement
/// audit) is being built concurrently in a sibling worktree. Docker is unavailable on the authoring
/// machine (standing project constraint), so NONE of this file's facts are verified locally, and
/// none may be reported as passing — they complete at the wave-2 gate.
/// </para>
///
/// <para>
/// <b>Two provisional wire-contract guesses, flagged so they are not mistaken for verified fact.</b>
/// (1) The read payload's scheduled-change field is assumed to be a <c>scheduled</c> object
/// mirroring the repository's already-landed <c>ScheduledEmployeeProfileChange</c> record
/// (<c>effectiveFrom</c> / <c>effectiveTo</c> / <c>partTimeFraction</c> / <c>position</c> /
/// <c>employmentCategory</c>), null when nothing is scheduled. (2) The OQ-6 request field is assumed
/// to be an optional boolean <c>carryForwardToScheduledChange</c> on the PUT body. NEITHER name is
/// fixed anywhere in code as of this writing (TASK-14104 owns the actual contract) — if the merged
/// implementation names them differently, these specific property-name assertions need reconciling
/// at the wave-2 gate; the BEHAVIOUR each test pins (what must be true, not what it is called) is not
/// in doubt.
/// </para>
///
/// <para>Conventions mirror <see cref="ProfileBackdatingEndpointTests"/> and
/// <see cref="EmployeeProfileLifecycleTests"/>: a per-test container, the WAF host, a fixed clock, a
/// GlobalAdmin token, direct DB seeding for anything that predates "today".</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ScheduledProfileChangeEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _fixedHost = null!;

    /// <summary>
    /// The ONE pinned "today" for every test in this suite (PAT-008), matching the sibling S141
    /// suites. 2025-03-12 — a WEDNESDAY, safely on the OK24 side of the 2026-04-01 OK24→OK26
    /// cutover. Every date below is DERIVED from <see cref="F"/>, never from the wall clock.
    /// </summary>
    private static readonly DateOnly F = new(2025, 3, 12);

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _fixedHost = _factory.WithFixedToday(F);
        _ = _fixedHost.CreateClient(); // PAT-008 boot order — fixtures seeded by [Fact]s come after
    }

    public async Task DisposeAsync()
    {
        _fixedHost?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public void Anchor_IsWednesday_OnOk24Side()
    {
        Assert.Equal(DayOfWeek.Wednesday, F.DayOfWeek);
        Assert.Equal("OK24", OkVersionResolver.ResolveVersion(F));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 1. B6 — the round-trip must be inert
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 1 (B6). The edit drawer's whole point is: read the current state, let HR change ONE
    /// field, save. If the read handed back a future value that has not taken effect, and the save
    /// blindly echoes it back dated today, the not-yet-effective value gets pulled forward and any
    /// already-booked absence in the interval is silently revalued at the wrong fraction. This test
    /// proves the round-trip is a true no-op: GET, PUT the EXACT same values straight back, and
    /// nothing on the timeline — including an absence sitting in the danger zone — may move.
    /// </summary>
    [Fact]
    public async Task GET_ThenPUT_Unmodified_WithScheduledChangePresent_IsInert_NoAbsenceRevalued()
    {
        var employeeId = await SeedEmployeeAsync();
        var scheduledFrom = F.AddDays(30);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, 0.800m, "Today", "Standard"),
            (scheduledFrom, null, 0.500m, "Future", "Standard"));

        // An absence inside TODAY's own interval — the exact spot B6's defect would revalue if the
        // drawer pulled the FUTURE fraction (0.500) forward into a today-dated write. F+9 = Friday
        // 2025-03-21, a weekday (a zero-norm day is rejected by the guard and skipped by revaluation
        // before it could even prove anything — S138/QUAL-153 — so the date must be a working day).
        var dangerZoneAbsence = F.AddDays(9);
        await SeedAbsenceAsync(employeeId, dangerZoneAbsence, "VACATION", hours: 7.4m, feriedage: 1.0m);
        Assert.True(dangerZoneAbsence.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday);

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        // Foundational premise: the drawer's read is TODAY's state, not the scheduled one (B1).
        Assert.Equal(0.800m, before.PartTimeFraction);
        Assert.Equal("Today", before.Position);

        var rsp = await PutProfileAsync(client, employeeId, F,
            partTimeFraction: before.PartTimeFraction, position: before.Position,
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: null);

        // Corrected at the sprint-end review: the earlier comment here described a failing
        // condition ("the still-refusing/not-yet-wired endpoint shape") that never existed in any
        // commit — this PUT is dated TODAY, so it was never subject to the future-date refusal
        // TASK-14104 lifted, in this file or any earlier state of it. The pin is a valid contract
        // check regardless: it proves the round-trip no-op end to end (B1's as-of-today read
        // composing correctly with the writer's same-values no-op), and it is simply unverified
        // here — Docker does not run on this machine, so no claim of RED or GREEN is made either way.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadProfileTimelineAsync(employeeId);
        Assert.Equal(2, rows.Count);
        Assert.Equal((F.AddDays(-400), scheduledFrom, 0.800m, "Today"), (rows[0].From, rows[0].To, rows[0].Fraction, rows[0].Position));
        Assert.Equal((scheduledFrom, (DateOnly?)null, 0.500m, "Future"), (rows[1].From, rows[1].To, rows[1].Fraction, rows[1].Position));

        // RED: fails if the round-trip split the covering row anyway (a third row would exist) or if
        // the aggregate token moved despite nothing changing.
        var after = await GetProfileAsync(client, employeeId);
        Assert.Equal(before.Version, after.Version);

        // RED: fails if the danger-zone absence was revalued under the pulled-forward 0.500 fraction
        // (7.4h at a half-time norm of 3.7 would read 2.0, not 1.0).
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, dangerZoneAbsence));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. B5 — the concurrency token, at the GET/PUT and GET/DELETE sites
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 2a (B5). Before this sprint the token was the open row's OWN version; with a scheduled
    /// row present the open row is the FUTURE one, so a per-row token would have the GET hand out
    /// one row's version while the PUT validated a different row's — every edit 412ing forever.
    /// <c>users.version</c> (owner ruling OQ-3 (a)) must agree at both sites.
    /// </summary>
    [Fact]
    public async Task GET_ThenPUT_WithScheduledChangePresent_Succeeds()
    {
        var employeeId = await SeedEmployeeAsync();
        var scheduledFrom = F.AddDays(30);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, 0.800m, "Today", "Standard"),
            (scheduledFrom, null, 0.500m, "Future", "Standard"));

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);

        var rsp = await PutProfileAsync(client, employeeId, F,
            partTimeFraction: before.PartTimeFraction, position: "TodayEdited",
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: null);

        // RED: fails with 412 ("someone else changed this") if the PUT still validates against the
        // open (future) row's own version while the GET handed out a different row's token.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    /// <summary>Pin 2b (B5). The same token must also agree at the DELETE site — the refinement's
    /// own finding is that listing only the GET and the PUT would have shipped a fix that left
    /// deletion broken for exactly the same reason.</summary>
    [Fact]
    public async Task GET_ThenDELETE_WithScheduledChangePresent_Succeeds()
    {
        var employeeId = await SeedEmployeeAsync();
        var scheduledFrom = F.AddDays(30);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, 0.800m, "Today", "Standard"),
            (scheduledFrom, null, 0.500m, "Future", "Standard"));

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);

        var rsp = await DeleteProfileAsync(client, employeeId, $"\"{before.Version}\"");

        // RED: fails with 412 if the DELETE's version predicate still matches against the open
        // (future) row rather than the token the GET actually handed out.
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. B4 / OQ-5 (a) — delete while a change is scheduled
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 3. Owner ruling OQ-5 (a): deleting a profile while a change is scheduled must (a) close
    /// the record covering today, (b) retire EVERY scheduled record (via the existing zero-width
    /// close idiom — never a hard delete, never an inverted interval), (c) record TODAY's values —
    /// not the future row's — as the audit's <c>previous_data</c>, and (d) audit the retirement
    /// itself, because a row that was audited into existence when it was scheduled must not vanish
    /// unrecorded.
    /// </summary>
    [Fact]
    public async Task DELETE_WithScheduledChangePresent_ClosesToday_RetiresScheduled_AuditsBoth()
    {
        var employeeId = await SeedEmployeeAsync();
        var scheduledFrom = F.AddDays(30);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, 0.800m, "Today", "Standard"),
            (scheduledFrom, null, 0.500m, "Future", "Standard"));

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        var scheduledProfileId = (await ReadProfileTimelineAsync(employeeId))
            .Single(r => r.From == scheduledFrom).ProfileId;

        var probeStartUtc = DateTimeOffset.UtcNow;
        var rsp = await DeleteProfileAsync(client, employeeId, $"\"{before.Version}\"");
        Assert.Equal(HttpStatusCode.NoContent, rsp.StatusCode);

        var rows = await ReadProfileTimelineAsync(employeeId);
        Assert.Equal(2, rows.Count);

        // (a) The record covering today is closed AT TODAY — not stamped onto the future row.
        var todayRow = rows.Single(r => r.From == F.AddDays(-400));
        Assert.Equal(F, todayRow.To);

        // (b) The scheduled record is retired by the ZERO-WIDTH close (effective_to == effective_from)
        //     — never an inverted interval (effective_to < effective_from would be worse: a bug the
        //     pre-S141 delete produced by stamping today's date onto the future row).
        var retiredRow = rows.Single(r => r.From == scheduledFrom);
        Assert.Equal(scheduledFrom, retiredRow.To);
        Assert.True(retiredRow.To >= retiredRow.From, "the retired interval must never invert.");

        // (c) The audit's previous_data reflects TODAY's values (0.800/"Today"), never the future
        //     row's (0.500/"Future") — this is what makes the audit trail honest about what was
        //     actually deleted.
        //
        //     Filtered by TODAY's OWN profile_id, not "the latest DELETED row" (sprint-end review
        //     W2). The handler writes the main row's audit entry FIRST and then one DELETED row PER
        //     RETIRED SCHEDULED ROW afterwards, so "latest by audit_id" is the retirement row — whose
        //     previous_data is the SCHEDULED row's values, not today's. Asserting against the latest
        //     row would therefore fail while production is correct, and a reader seeing it fail would
        //     conclude the audit recorded the future row's values — exactly the pre-sprint defect
        //     this area was fixed for. Filtering by the row's own identity avoids depending on write
        //     order at all.
        var previousData = await ReadProfileAuditPreviousDataAsync(todayRow.ProfileId, "DELETED");
        Assert.NotNull(previousData);
        using var prev = JsonDocument.Parse(previousData!);
        Assert.Equal(0.800m, prev.RootElement.GetProperty("partTimeFraction").GetDecimal());

        // (d) The retirement of the SCHEDULED row is itself audited — a row that was audited into
        //     existence must not vanish unrecorded. The exact destination (the domain
        //     employee_profile_audit table vs. the generic ADR-026 audit_projection table) is
        //     TASK-14104's call — this checks BOTH, so the pin is honest about the contract being
        //     undetermined rather than betting on one table and failing the other reasonable choice.
        var retirementAudited = await RetirementIsAuditedAsync(employeeId, scheduledProfileId, probeStartUtc);
        Assert.True(retirementAudited,
            "expected a NEW audit trail entry (domain table or ADR-026 projection) documenting the " +
            "retirement of the scheduled row — none was found.");
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4. OQ-6 (a) — a today-dated edit while a change is scheduled
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 4a (OQ-6, branch 1 — "apply until the scheduled change"). This is the pre-existing split
    /// behaviour: a today-dated edit produces a row that ends where the scheduled change begins, and
    /// the scheduled row itself is untouched. Pinned explicitly (not just assumed) so a regression in
    /// either branch is visible on its own.
    /// </summary>
    [Fact]
    public async Task PUT_TodayDatedEdit_ApplyUntilScheduledChange_LeavesScheduledRowUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        var scheduledFrom = F.AddDays(30);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, 1.000m, "OldTitle", "Standard"),
            (scheduledFrom, null, 0.600m, "FutureTitle", "Standard"));

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, F,
            partTimeFraction: before.PartTimeFraction, position: "NewTitle",
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: false);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadProfileTimelineAsync(employeeId);
        // The seeded "today" row runs [F-400, scheduledFrom) — it does NOT start today, so this
        // edit (EffectiveFrom = F) cannot route to an in-place update (that requires the covering
        // row to start exactly on the requested date, TemporalWriteRouter.cs's B' case). It is a
        // genuine SplitCovering (C'): the seeded row is closed at F (kept as CLOSED history,
        // "OldTitle") and a NEW row [F, scheduledFrom) is inserted with "NewTitle" — the "before"
        // branch's documented behaviour ("unchanged behaviour; the write may truncate a later
        // scheduled change and the existing choice still applies"). So the timeline now holds
        // THREE rows total: the closed predecessor, the new today-row, and the untouched scheduled
        // row — not two. (This count is unrelated to the OQ-3 token move; it is the pre-existing
        // split-routing consequence of editing a date that is not the covering row's own start.)
        Assert.Equal(3, rows.Count);
        var todayRow = rows.Single(r => r.From == F);
        Assert.Equal(scheduledFrom, todayRow.To);
        Assert.Equal("NewTitle", todayRow.Position);

        // RED: fails if the edit silently reached into the scheduled row (position or fraction moved
        // off its scheduled values) instead of expiring cleanly at the scheduled date.
        var scheduledRow = rows.Single(r => r.From == scheduledFrom);
        Assert.Equal("FutureTitle", scheduledRow.Position);
        Assert.Equal(0.600m, scheduledRow.Fraction);
    }

    /// <summary>
    /// Pin 4b (OQ-6, branch 2 — "carry forward", and the field-scoping half of the ruling). HR edits
    /// ONLY the position today; asking to carry it forward must move the SAME field on the scheduled
    /// row and MUST NOT touch the fraction HR never edited — otherwise a colleague's scheduled
    /// decision about the fraction is silently discarded as a side effect of an unrelated edit.
    /// </summary>
    [Fact]
    public async Task PUT_TodayDatedEdit_CarryForward_TouchesOnlyTheEditedField()
    {
        var employeeId = await SeedEmployeeAsync();
        var scheduledFrom = F.AddDays(30);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, 1.000m, "OldTitle", "Standard"),
            (scheduledFrom, null, 0.600m, "FutureTitle", "Standard"));

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        // Fraction is sent UNCHANGED (1.000, equal to today's value) — only the position is a real
        // edit. Carry-forward must therefore move ONLY the position on the scheduled row.
        var rsp = await PutProfileAsync(client, employeeId, F,
            partTimeFraction: before.PartTimeFraction, position: "NewTitle",
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: true);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var rows = await ReadProfileTimelineAsync(employeeId);
        var scheduledRow = rows.Single(r => r.From == scheduledFrom);

        // RED: fails if carry-forward is implemented as "copy the whole request onto the scheduled
        // row" — the fraction would then read 1.000 instead of staying at the colleague's chosen 0.600.
        Assert.Equal("NewTitle", scheduledRow.Position);
        Assert.Equal(0.600m, scheduledRow.Fraction);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5. B0 — the read payload carries the scheduled change
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 5 (B0, profile side). The GET must carry the next scheduled change in the SAME payload —
    /// not require a second call — so every present and future screen shows it. Pinned both ways:
    /// present when a change is scheduled, and explicitly null (not merely absent) when nothing is.
    /// </summary>
    [Fact]
    public async Task GET_WithScheduledChangePresent_PayloadCarriesIt()
    {
        var employeeId = await SeedEmployeeAsync();
        var scheduledFrom = F.AddDays(30);
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), scheduledFrom, 0.800m, "Today", "Standard"),
            (scheduledFrom, null, 0.500m, "FutureTitle", "Standard"));

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // RED: fails if the read payload has no "scheduled" member at all (a screen would then have
        // to poll a second endpoint, which is precisely the bolt-on B0 exists to rule out).
        Assert.True(body.TryGetProperty("scheduled", out var scheduled),
            "expected the GET payload to carry the next scheduled change (B0).");
        Assert.NotEqual(JsonValueKind.Null, scheduled.ValueKind);
        Assert.Equal(scheduledFrom.ToString("yyyy-MM-dd"), scheduled.GetProperty("effectiveFrom").GetString());
        Assert.Equal(0.500m, scheduled.GetProperty("partTimeFraction").GetDecimal());
        Assert.Equal("FutureTitle", scheduled.GetProperty("position").GetString());
    }

    /// <summary>The negative case: nothing scheduled must read as an explicit null, not merely an
    /// absent property — otherwise a client cannot tell "nothing scheduled" from "server too old to
    /// know about scheduling" by inspecting the same field.</summary>
    [Fact]
    public async Task GET_WithNoScheduledChange_PayloadCarriesExplicitNull()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (F.AddDays(-400), null, 1.000m, "Only", "Standard"));

        var client = AdminClient();
        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("scheduled", out var scheduled));
        Assert.Equal(JsonValueKind.Null, scheduled.ValueKind);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7. A future write leaves the employment_category cache untouched
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 7 (profile half). <c>users.employment_category</c> means "the category as of TODAY". A
    /// future-dated write must leave it alone until the scheduled date actually arrives — this needs
    /// the endpoint's own future-date refusal LIFTED (TASK-14104, wave 2), which is why it cannot be
    /// exercised through the live endpoint before that merges.
    /// </summary>
    [Fact]
    public async Task PUT_FutureDated_LeavesLiveEmploymentCategoryCacheUntouched()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (F.AddDays(-400), null, 1.000m, "Today", "Standard"));

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        var scheduledFrom = F.AddDays(30);
        var rsp = await PutProfileAsync(client, employeeId, scheduledFrom,
            partTimeFraction: 1.000m, position: "Today",
            employmentCategory: "Fuldmægtig", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: null);

        // RED (wave-2 dependent): today the endpoint still refuses a future EffectiveFrom with 422 —
        // this fails until TASK-14104 lifts the endpoint-side guard.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The scheduled row carries the new category from its own start date …
        Assert.Equal("Fuldmægtig", await ReadDatedCategoryAtAsync(employeeId, scheduledFrom));
        // … but the LIVE cache — what "as of today" reads use — must not have moved yet.
        Assert.Equal("Standard", await ReadUsersCategoryAsync(employeeId));
        Assert.Equal("Standard", await ReadDatedCategoryAtAsync(employeeId, F));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 8. The revaluation rule, all three parts
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 8. A future-dated change must revalue every absence dated ON OR AFTER its effective date,
    /// leave every EARLIER absence untouched, and STOP at the next already-scheduled row's start —
    /// it must not reach past a colleague's separately scheduled change. All three in one scenario so
    /// the boundary behaviour is unambiguous: a covering row [start, F+30) is split by a NEW
    /// future-dated write at F+10, while an EXISTING further-future row at F+30 (unrelated to this
    /// write) is left standing.
    /// </summary>
    [Fact]
    public async Task PUT_FutureDated_RevaluesFromItsEffectiveDate_NotBefore_NotPastNextScheduledRow()
    {
        var employeeId = await SeedEmployeeAsync();
        var writeFrom = F.AddDays(10);   // Saturday, a row date only — not an absence date
        var nextScheduledFrom = F.AddDays(30); // Friday, likewise a row date only
        await ReplaceProfileTimelineAsync(employeeId,
            (F.AddDays(-400), nextScheduledFrom, 1.000m, "Base", "Standard"),
            (nextScheduledFrom, null, 0.250m, "AlreadyScheduled", "Standard"));

        var beforeWindow = F.AddDays(5);     // Monday
        var insideWindow = F.AddDays(15);    // Thursday
        var pastNextScheduled = F.AddDays(35); // Wednesday — inside the ALREADY-scheduled row's interval
        foreach (var d in new[] { beforeWindow, insideWindow, pastNextScheduled })
            Assert.True(d.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday, $"{d} must be a weekday (S138/QUAL-153).");

        await SeedAbsenceAsync(employeeId, beforeWindow, "VACATION", hours: 7.4m, feriedage: 1.0m);
        await SeedAbsenceAsync(employeeId, insideWindow, "VACATION", hours: 7.4m, feriedage: 1.0m);
        await SeedAbsenceAsync(employeeId, pastNextScheduled, "VACATION", hours: 7.4m, feriedage: 1.0m);

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, writeFrom,
            partTimeFraction: 0.500m, position: "NewlyScheduled",
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: null);

        // RED (wave-2 dependent): fails with 422 until TASK-14104 lifts the endpoint's future-date
        // refusal — this write is dated 10 days ahead.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // Before the written interval: untouched.
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, beforeWindow));
        // Inside [writeFrom, nextScheduledFrom): revalued at the new 0.500 fraction (7.4h / 3.7 = 2.0).
        Assert.Equal(2.0m, await ReadFeriedageAsync(employeeId, insideWindow));
        // Past the NEXT scheduled row's start: untouched by THIS write — it belongs to the
        // already-scheduled 0.250 interval, which this write must not reach into.
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, pastNextScheduled));
    }

    // ═════════════════════════════════════════════════════════════════════
    // 9. The worklist rule — conditional, not to be over-pinned
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pin 9a. A future date in a LATER month produces no worklist rows, even when that later month
    /// itself already has an export record — the exported-month scan's own upper bound (the first
    /// day of the month AFTER today) structurally excludes a later month regardless of its export
    /// state, so this is not a coincidence of "nothing to find".
    /// </summary>
    [Fact]
    public async Task PUT_FutureDated_InALaterMonth_RaisesNoWorklistRows()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (F.AddDays(-400), null, 1.000m, "Today", "Standard"));
        var laterMonthDate = F.AddMonths(2); // safely in a later month than F (March 2025)
        Assert.True(laterMonthDate.Month != F.Month || laterMonthDate.Year != F.Year);
        await SeedExportRecordAsync(employeeId, laterMonthDate.Year, laterMonthDate.Month);
        await SeedExportRecordAsync(employeeId, F.Year, F.Month); // this month's export must ALSO not fire

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, laterMonthDate,
            partTimeFraction: 0.500m, position: "Scheduled",
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: null);

        // RED (wave-2 dependent): fails with 422 until the endpoint accepts a future EffectiveFrom.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Empty(await ReadWorklistRowsAsync(employeeId, "EXPORTED_MONTH"));
        Assert.Empty(await ReadWorklistRowsAsync(employeeId, "SETTLED_YEAR"));
    }

    /// <summary>
    /// Pin 9b. A future date INSIDE the current month must leave the existing exported-month rule
    /// UNCHANGED — it may legitimately raise a row, because the month is already exported and this
    /// write's interval genuinely reaches into it. This is the counter-test to 9a: it proves the
    /// "no worklist rows" pin above is not a blanket suppression that would hide a real finding.
    /// </summary>
    [Fact]
    public async Task PUT_FutureDated_InsideTheCurrentMonth_StillRaisesTheExportedMonthRow()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (F.AddDays(-400), null, 1.000m, "Today", "Standard"));
        await SeedExportRecordAsync(employeeId, F.Year, F.Month);

        var withinMonth = new DateOnly(F.Year, F.Month, 1).AddDays(
            DateTime.DaysInMonth(F.Year, F.Month) - 2); // a few days after F, same month
        Assert.True(withinMonth > F && withinMonth.Month == F.Month);

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        var rsp = await PutProfileAsync(client, employeeId, withinMonth,
            partTimeFraction: 0.500m, position: "Scheduled",
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: null);

        // RED (wave-2 dependent): fails with 422 until the endpoint accepts a future EffectiveFrom.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // RED: fails if the future-date carve-out was implemented too broadly and suppresses THIS
        // legitimate, same-month exported-month finding along with the later-month case above.
        var rows = await ReadWorklistRowsAsync(employeeId, "EXPORTED_MONTH");
        var row = Assert.Single(rows);
        Assert.Equal(F.Year, row.Year);
        Assert.Equal(F.Month, row.Month);
    }

    /// <summary>
    /// Pin 9c. No SETTLED_YEAR row can arise from the DATE-based rule for ANY future-dated write: an
    /// active settlement's valuation boundary is always in the past, so "the correction starts before
    /// the boundary" is false whenever the correction itself starts in the future. No absence is
    /// seeded here on purpose, so only the date-based path is exercised.
    ///
    /// <para>
    /// <b>Resolved by trace, 2026-09-14 (owner/coordinator correction to the pin register).</b> The
    /// separate, UNCONDITIONAL "skip" path (<c>WriteForSkippedSettledYearsAsync</c>) is a DIFFERENT
    /// mechanism and is genuinely reachable for a future-dated write — see
    /// <see cref="PUT_FutureDated_ReachingATerminationSettledYear_DoesRaiseASettledYearRow_ViaTheSkipPath"/>
    /// immediately below, which pins that it is reachable and that the resulting row is CORRECT, not
    /// spurious. This test's scope is therefore narrowed on purpose to the date-based rule alone —
    /// "no settlement row for any future date" was never quite true; "no settlement row from the
    /// DATE rule" is, and the counter-test below is what stops a future reader from "fixing" the skip
    /// path into date-awareness and silencing a true finding.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PUT_FutureDated_NeverRaisesASettledYearRow_ViaTheDateRule()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (F.AddDays(-400), null, 1.000m, "Today", "Standard"));
        // An active VACATION settlement whose boundary is (as every settlement's always is) in the
        // past, so the date rule's "correction starts before the boundary" test cannot fire once the
        // correction itself is dated in the future.
        await SeedActiveSettlementAsync(employeeId, "VACATION", F.Year, F.AddDays(-30));

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        var futureDate = F.AddDays(20);
        var rsp = await PutProfileAsync(client, employeeId, futureDate,
            partTimeFraction: 0.500m, position: "Scheduled",
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: null);

        // RED (wave-2 dependent): fails with 422 until the endpoint accepts a future EffectiveFrom.
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Empty(await ReadWorklistRowsAsync(employeeId, "SETTLED_YEAR"));
    }

    /// <summary>
    /// Pin 9d — the counter-test to 9c, requested after a read-only trace settled the skip-path
    /// question. The DATE rule cannot fire for a future-dated write (9c); the SKIP path is a
    /// different, UNCONDITIONAL mechanism, and it CAN and SHOULD fire.
    ///
    /// <para>
    /// <b>The mechanism, in plain language.</b> A holiday year can be settled EARLY through the
    /// termination trigger, which crystallises at the leaver's end date rather than at the year's
    /// natural boundary. Nothing caps a profile effective date past someone's employment end (B2a),
    /// and nothing purges a holiday booking made before they resigned. So: someone resigns, having
    /// already booked a trip for a date that is still in the future; HR later schedules a fraction
    /// correction dated after the resignation (and, in this pin, still in the future relative to
    /// TODAY); the correction's revaluation interval reaches that booking; the booking belongs to a
    /// year the termination has already settled. The revaluation correctly DECLINES to rewrite a
    /// value a frozen settlement protects — and reporting that decline on the HR worklist is exactly
    /// what the skip path exists to do. The resulting row is CORRECT, not a defect.
    /// </para>
    ///
    /// <para>
    /// <b>Scope, per the coordinator's correction.</b> The skip path is reachable ONLY through the
    /// profile write — an agreement-code change runs no revaluation, so it can never produce a
    /// skipped group. This pin is profile-only for that reason, not by omission.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PUT_FutureDated_ReachingATerminationSettledYear_DoesRaiseASettledYearRow_ViaTheSkipPath()
    {
        var employeeId = await SeedEmployeeAsync();
        await ReplaceProfileTimelineAsync(employeeId, (F.AddDays(-400), null, 1.000m, "Base", "Standard"));

        // Terminated 60 days before today (2025-01-11) — a real leaver, not a hypothetical one.
        var terminationDate = F.AddDays(-60);
        await MarkTerminatedAsync(employeeId, terminationDate);

        // A trip booked before they resigned, for a date still in the future relative to TODAY:
        // 2025-06-10, a Tuesday (independently verified: 2025-01-01 is a Wednesday, day-of-year 161
        // is 160 days later, 160 mod 7 = 6, Wednesday + 6 = Tuesday) — a weekday, so the zero-norm
        // guard (S138/QUAL-153) cannot suppress it before the settlement logic ever sees it.
        var futureAbsence = F.AddDays(90);
        Assert.Equal(new DateOnly(2025, 6, 10), futureAbsence);
        Assert.Equal(DayOfWeek.Tuesday, futureAbsence.DayOfWeek);
        await SeedAbsenceAsync(employeeId, futureAbsence, "VACATION", hours: 7.4m, feriedage: 1.0m);

        // The ferieår VACATION accrues under reset_month = 9 (the schema's own CHECK — VACATION's
        // reset_month is fixed at September). June 2025 < September, so it belongs to ferieår 2024 —
        // the year this employee's TERMINATION already settled.
        const int resetMonth = 9;
        var settledYear = futureAbsence.Month >= resetMonth ? futureAbsence.Year : futureAbsence.Year - 1;
        Assert.Equal(2024, settledYear);
        await SeedActiveSettlementAsync(
            employeeId, "VACATION", settledYear, terminationDate, trigger: "TERMINATION");

        var client = AdminClient();
        var before = await GetProfileAsync(client, employeeId);
        // A FUTURE-dated write (relative to TODAY) that changes the fraction, dated between today
        // and the booked trip so its (open-ended) revaluation interval reaches the trip.
        var scheduledFrom = F.AddDays(10);
        Assert.True(scheduledFrom < futureAbsence, "the write must precede the absence it is meant to reach.");
        var rsp = await PutProfileAsync(client, employeeId, scheduledFrom,
            partTimeFraction: 0.500m, position: "Base",
            employmentCategory: "Standard", ifMatch: $"\"{before.Version}\"",
            carryForwardToScheduledChange: null);

        // RED (wave-2 dependent — TASK-14104 has merged, so the endpoint's future-date refusal is
        // lifted; this is the same status the other future-dated pins in this file already expect).
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // The skip actually happened: the settled year's absence keeps its recorded feriedage instead
        // of being silently rewritten to the new fraction's value (which would be 2.0, not 1.0).
        Assert.Equal(1.0m, await ReadFeriedageAsync(employeeId, futureAbsence));

        // RED: fails if the withheld correction goes unreported — a future reader who "fixed" the
        // skip path to also require a past-dated correction would make this assertion fail, which is
        // precisely the regression this pin exists to catch.
        var row = Assert.Single(await ReadSettledYearWorklistRowsAsync(employeeId));
        Assert.Equal("VACATION", row.EntitlementType);
        Assert.Equal(settledYear, row.EntitlementYear);
        Assert.Contains("PROFILE_CHANGE", row.TriggersJson, StringComparison.Ordinal);
    }

    // ─── Seeding helpers ─────────────────────────────────────────────────

    private async Task<string> SeedEmployeeAsync(DateOnly? employmentStartDate = null)
    {
        var employeeId = "emp_s141_pf_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, employment_category,
                               employment_start_date, is_active)
            VALUES (@u, @u, 'dev-only', 'S141 Scheduled Profile Test User', NULL,
                    @org, 'AC', 'OK24', 'Standard', @start, TRUE)
            """, conn))
        {
            userCmd.Parameters.AddWithValue("u", employeeId);
            userCmd.Parameters.AddWithValue("org", OrgId);
            userCmd.Parameters.AddWithValue("start", (object?)employmentStartDate ?? DBNull.Value);
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

    private async Task ReplaceProfileTimelineAsync(
        string employeeId,
        params (DateOnly From, DateOnly? To, decimal Fraction, string? Position, string Category)[] rows)
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
                VALUES (gen_random_uuid(), @e, @f, @p, @cat, @from, @to, @v)
                """, conn);
            ins.Parameters.AddWithValue("e", employeeId);
            ins.Parameters.AddWithValue("f", row.Fraction);
            ins.Parameters.AddWithValue("p", (object?)row.Position ?? DBNull.Value);
            ins.Parameters.AddWithValue("cat", row.Category);
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
        cmd.Parameters.AddWithValue("hash", $"hash-{year}-{month}-{Guid.NewGuid():N}");
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Sets the employee's <c>employment_end_date</c> and deactivates them — the fact that
    /// makes the TERMINATION settlement trigger (as opposed to YEAR_END) a real scenario rather than
    /// a fixture artefact. B2a: there is no ceiling stopping a later profile write dated past this
    /// date, which is the whole precondition for pin 9d's scenario.</summary>
    private async Task MarkTerminatedAsync(string employeeId, DateOnly endDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_end_date = @end, is_active = FALSE WHERE user_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("end", endDate);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary><paramref name="trigger"/> — 'YEAR_END' (default, matches the pre-existing pins) or
    /// 'TERMINATION' (pin 9d — a year settled early because the employee left, not because the year
    /// itself ended).</summary>
    private async Task SeedActiveSettlementAsync(
        string employeeId, string entitlementType, int year, DateOnly boundaryDate,
        string trigger = "YEAR_END")
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot)
            VALUES (@e, @t, @y, 1, 'SETTLED', @trigger,
                    jsonb_build_object('settlementBoundaryDate', to_char(@b::date, 'YYYY-MM-DD')))
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", entitlementType);
        cmd.Parameters.AddWithValue("trigger", trigger);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("b", boundaryDate);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── Read helpers ────────────────────────────────────────────────────

    private sealed record ProfileRow(
        Guid ProfileId, DateOnly From, DateOnly? To, decimal Fraction, string? Position, string? Category);

    private async Task<IReadOnlyList<ProfileRow>> ReadProfileTimelineAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT profile_id, effective_from, effective_to, part_time_fraction, position, employment_category
            FROM employee_profiles WHERE employee_id = @e ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var rows = new List<ProfileRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new ProfileRow(
                reader.GetGuid(0),
                reader.GetFieldValue<DateOnly>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }
        return rows;
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

    private sealed record WorklistRow(int? Year, int? Month);

    private async Task<IReadOnlyList<WorklistRow>> ReadWorklistRowsAsync(string employeeId, string kind)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT year, month FROM hr_backdate_worklist
            WHERE employee_id = @e AND kind = @k
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("k", kind);
        var rows = new List<WorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new WorklistRow(
                reader.IsDBNull(0) ? null : reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1)));
        }
        return rows;
    }

    private sealed record SettledYearWorklistRow(string EntitlementType, int EntitlementYear, string TriggersJson);

    /// <summary>The SETTLED_YEAR-specific read (pin 9d), carrying the (type, year) key and the
    /// triggers array — the exported-month read above has no use for either.</summary>
    private async Task<IReadOnlyList<SettledYearWorklistRow>> ReadSettledYearWorklistRowsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT entitlement_type, entitlement_year, triggers::text
            FROM hr_backdate_worklist
            WHERE employee_id = @e AND kind = 'SETTLED_YEAR'
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        var rows = new List<SettledYearWorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SettledYearWorklistRow(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
        }
        return rows;
    }

    /// <summary>
    /// The audit row for ONE specific profile_id, not "the latest by this employee" (sprint-end
    /// review W2) — a DELETE with a scheduled row present writes MULTIPLE audit rows (the main
    /// closed-today row, then one per retired scheduled row), so "latest" is ambiguous and, in the
    /// order this handler writes them, is actually the WRONG one for a test that means to check
    /// today's own row.
    /// </summary>
    private async Task<string?> ReadProfileAuditPreviousDataAsync(Guid profileId, string action)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT previous_data::text FROM employee_profile_audit
            WHERE profile_id = @p AND action = @a ORDER BY audit_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("p", profileId);
        cmd.Parameters.AddWithValue("a", action);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(),
            $"expected an employee_profile_audit row for profile_id='{profileId}' with action='{action}'.");
        return reader.IsDBNull(0) ? null : reader.GetString(0);
    }

    /// <summary>
    /// Checks BOTH plausible destinations for the mandatory retirement audit (owner ruling OQ-5 (a)):
    /// a second <c>employee_profile_audit</c> row keyed by the RETIRED row's own <c>profile_id</c>,
    /// or a NEW <c>audit_projection</c> (ADR-026) row for this employee whose event_type is not the
    /// ordinary soft-delete event, recorded after this probe started. Either is a legitimate
    /// implementation of "the retirement is separately audited" — the exact table is TASK-14104's
    /// call, not this test's.
    /// </summary>
    private async Task<bool> RetirementIsAuditedAsync(
        string employeeId, Guid retiredProfileId, DateTimeOffset probeStartUtc)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var domainCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM employee_profile_audit WHERE profile_id = @p", conn))
        {
            domainCmd.Parameters.AddWithValue("p", retiredProfileId);
            if (Convert.ToInt64(await domainCmd.ExecuteScalarAsync()) > 0)
                return true;
        }

        await using (var projectionCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM audit_projection
            WHERE target_resource_id = @e
              AND event_type <> 'EmployeeProfileSoftDeleted'
              AND occurred_at >= @since
            """, conn))
        {
            projectionCmd.Parameters.AddWithValue("e", employeeId);
            projectionCmd.Parameters.AddWithValue("since", probeStartUtc);
            return Convert.ToInt64(await projectionCmd.ExecuteScalarAsync()) > 0;
        }
    }

    // ─── HTTP helpers ────────────────────────────────────────────────────

    private HttpClient AdminClient()
    {
        var client = _fixedHost.CreateClient();
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
            employeeId: "ADMIN_S141_PF",
            name: "S141 Scheduled Profile Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    private sealed record ProfileGetResult(long Version, decimal PartTimeFraction, string? Position);

    private static async Task<ProfileGetResult> GetProfileAsync(HttpClient client, string employeeId)
    {
        var rsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        return new ProfileGetResult(
            body.GetProperty("version").GetInt64(),
            body.GetProperty("partTimeFraction").GetDecimal(),
            body.TryGetProperty("position", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null);
    }

    /// <summary>
    /// <paramref name="carryForwardToScheduledChange"/> is a PROVISIONAL field name (see the class
    /// doc) for OQ-6's request-side choice — omitted/null means "let the endpoint's default (apply
    /// until the scheduled change) stand", <c>true</c> means "carry the edited field into it too".
    /// </summary>
    private static async Task<HttpResponseMessage> PutProfileAsync(
        HttpClient client, string employeeId, DateOnly effectiveFrom,
        decimal partTimeFraction, string? position, string? employmentCategory, string ifMatch,
        bool? carryForwardToScheduledChange)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
                partTimeFraction,
                position,
                employmentCategory,
                carryForwardToScheduledChange,
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> DeleteProfileAsync(
        HttpClient client, string employeeId, string ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }
}
