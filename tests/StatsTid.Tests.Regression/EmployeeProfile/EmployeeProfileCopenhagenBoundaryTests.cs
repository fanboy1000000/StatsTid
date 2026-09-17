using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.EmployeeProfile;

/// <summary>
/// S142 / TASK-14205 — the DISCRIMINATING pins for census rows 15, 16, 17, 21, 46 and 47: the
/// employee-profile, employment-history and entitlement-eligibility surfaces now answer "what day is
/// it?" with the <b>Copenhagen business day</b>, not the UTC calendar day.
///
/// <para>
/// <b>THE PROBLEM, in plain language.</b> Every user of StatsTid is in Denmark, and Denmark runs one
/// hour ahead of UTC in winter (CET) and two in summer (CEST). So for the one-to-two hours between
/// Danish midnight and UTC midnight, the UTC calendar is still on YESTERDAY. Until S142 the product
/// derived its business dates from that UTC calendar, so an HR user working at 00:30 Danish time was
/// served a "today" of yesterday — and business dates are not cosmetic: they are the
/// <c>effective_from</c> a change is recorded at, the <c>effective_to</c> a profile is retired at,
/// and the predicate that decides whether a dated row is history, in force, or still to come.
/// </para>
///
/// <para>
/// <b>★ WHY THESE FACTS USE <c>WithFixedInstant</c> AND NOT <c>WithFixedToday</c>.</b>
/// <c>WithFixedToday(DateOnly)</c> pins UTC MIDNIGHT, which is the one instant of every day where
/// the two calendars are guaranteed to AGREE (Denmark's offset is never negative, so Copenhagen's
/// local midnight never falls before UTC midnight of the same date). A pin built on it therefore
/// passes under BOTH the old and the new implementation — it cannot fail, which makes it worthless
/// as evidence of this particular change. Every fact below pins an exact INSTANT from
/// <see cref="BoundaryInstants"/>, chosen so the UTC day and the Copenhagen day DISAGREE, and asserts
/// the Danish day as a hard-coded LITERAL. Nothing here is computed by calling
/// <c>CopenhagenBusinessDate</c>: a test that derives its expectation from the helper under test
/// proves only that the helper agrees with itself.
/// </para>
///
/// <para>
/// <b>Both seasons are exercised on purpose.</b> The summer instant (2026-07-15 22:30Z, Danish
/// 16 July) fails a raw-UTC implementation AND a plausible hard-coded <c>+01:00</c> one; the winter
/// instant (2026-01-15 23:30Z, Danish 16 January) fails a raw-UTC implementation while a hard-coded
/// <c>+01:00</c> would pass. Using only one season would leave "hardcodes winter's offset all year"
/// — the exact QUAL-005 bug <c>CopenhagenBusinessDate</c> exists to prevent — undetected.
/// </para>
///
/// <para>
/// <b>RED-FIRST, and stated honestly.</b> Docker is unavailable on the authoring machine, so nothing
/// in this class has been OBSERVED to run: each fact's RED condition is derived from the production
/// code as read, and is written down in that fact's doc comment. These are CI-verified in the
/// sprint-close watched run and are <b>NOT</b> claimed green locally. The same standard the sibling
/// <c>HrFollowUp\EffectiveDateBoundaryTests</c> set in S141.
/// </para>
///
/// <para>
/// <b>What is deliberately NOT asserted: instants.</b> S142 moves BUSINESS DATES only.
/// <c>created_at</c>, <c>updated_at</c>, audit timestamps and outbox ordering stay UTC instants,
/// because moving one of those would corrupt the audit chain and event ordering. No fact below reads
/// or asserts a timestamp column.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeProfileCopenhagenBoundaryTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgA = "STY_S142_CPH";

    // ── The two answers, as LITERALS. Never derived from CopenhagenBusinessDate. ──

    /// <summary>
    /// The Copenhagen calendar day at <see cref="BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen"/>
    /// (2026-07-15 22:30Z + CEST 2h = 2026-07-16 00:30 local). The RIGHT answer after S142.
    /// </summary>
    private static readonly DateOnly DanishSummerDay = new(2026, 7, 16);

    /// <summary>
    /// The UTC calendar day at the same instant — the answer the product gave BEFORE S142, written
    /// down so each fact can say precisely which value it is refusing.
    /// </summary>
    private static readonly DateOnly UtcSummerDay = new(2026, 7, 15);

    /// <summary>
    /// The Copenhagen calendar day at <see cref="BoundaryInstants.WinterEveningAlreadyTomorrowInCopenhagen"/>
    /// (2026-01-15 23:30Z + CET 1h = 2026-01-16 00:30 local).
    /// </summary>
    private static readonly DateOnly DanishWinterDay = new(2026, 1, 16);

    /// <summary>The UTC calendar day at that same winter instant — the pre-S142 answer.</summary>
    private static readonly DateOnly UtcWinterDay = new(2026, 1, 15);

    /// <summary>A date safely in the past of every fact — the anchor for a backdated correction.</summary>
    private static readonly DateOnly SummerCorrectionDate = new(2026, 7, 5);

    /// <summary>
    /// A LOCAL boundary instant, defined here because the three shared <see cref="BoundaryInstants"/>
    /// are all mid-month and therefore discriminate the DAY but not the MONTH.
    /// 2026-07-31 22:30Z + CEST (+02:00) = 2026-08-01 00:30 local — so the UTC calendar says 31 July
    /// while Copenhagen has already turned over into August.
    ///
    /// <para><b>Why a month boundary earns its own fact.</b> A close date that is wrong by one day is
    /// already a defect; a close date that is wrong by one MONTH is the payroll-visible version of the
    /// same defect, because every export, settlement and approval period in this product is bounded by
    /// month. Off by a day inside a month, most downstream reads still land in the right period; across
    /// a month boundary, none of them do.</para>
    /// </summary>
    private static readonly DateTimeOffset SummerMonthEndAlreadyNextMonthInCopenhagen =
        BoundaryInstants.SummerMonthEndAlreadyNextMonthInCopenhagen; // promoted at the wave-2 merge: two tasks had defined this independently

    /// <summary>The Copenhagen day at <see cref="SummerMonthEndAlreadyNextMonthInCopenhagen"/> — a
    /// literal, and in a DIFFERENT MONTH from the UTC day (2026-07-31) at the same instant.</summary>
    private static readonly DateOnly DanishNextMonthDay = new(2026, 8, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Deliberately NOT booted here: each fact builds its own instant-pinned host and seeds
        // AFTER that host's first CreateClient(), which re-runs Program.cs's startup seeders.
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static int Seq;
    private static string NextId(string prefix) => $"{prefix}_{Interlocked.Increment(ref Seq)}";

    // ════════════════════════════════════════════════════════════════════════════
    // ★ THE MARQUEE — census row 15 (EmployeeProfileEndpoints' `today`) + row 46
    //   (EmployeeProfileRepository.Today). A WRITE defect, not a display one.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A backdated correction made after Danish midnight must NOT carry into the row that begins
    /// TODAY — because that row is in force, not scheduled.
    ///
    /// <para><b>The scenario, in plain language.</b> An employee's profile timeline is split on
    /// 16 July: the old row ends there and a new one ("Specialkonsulent") begins there. At 00:30
    /// Danish time on 16 July — so that row is IN FORCE — HR makes an unrelated correction dated
    /// 5 July and ticks the OQ-6 box that means "and carry this into the change that is still
    /// scheduled". There is no scheduled change: 16 July has arrived. The correct outcome is that
    /// the 16 July row is left alone.</para>
    ///
    /// <para><b>RED on the pre-S142 code, and why it matters.</b> The endpoint's `today` was the UTC
    /// day, i.e. 15 July. The OQ-6 test is literally <c>boundary &gt; today</c>, and the correction's
    /// truncating boundary IS 16 July, so <c>16 July &gt; 15 July</c> read TRUE: the handler
    /// classified a row that had already taken effect as a future scheduled change and performed a
    /// SECOND routed write into it, rewriting its position to "Chefkonsulent". That is a durable,
    /// un-asked-for change to a live row — HR asked to update the future and the product updated the
    /// present. The first assertion below is exactly that row's position.</para>
    ///
    /// <para>The two response assertions fail on the pre-S142 code as well, for the same root cause:
    /// the body reports the state as of the UTC day (the freshly written 5–16 July row), and reports
    /// the already-in-force 16 July row as still <c>scheduled</c>.</para>
    /// </summary>
    [Fact]
    public async Task Put_AfterDanishMidnight_DoesNotCarryTheEditIntoARowThatIsAlreadyInForce()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        using var client = Client(host, GlobalAdminToken());

        var employeeId = NextId("cph_carry");
        await SeedSplitTimelineAsync(employeeId, DanishSummerDay);

        // The aggregate token (ADR-019: ONE token per employee = users.version) comes from the GET,
        // which is the only place a caller can get it. Both calendars have a row covering their own
        // "today" here, so this GET is 200 either way — the fact discriminates on the WRITE, not on
        // whether the read succeeded.
        var getRsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);
        var ifMatch = getRsp.Headers.ETag!.Tag;

        var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(new
            {
                effectiveFrom = SummerCorrectionDate,
                partTimeFraction = 1.000m,
                position = "Chefkonsulent",
                carryForwardToScheduledChange = true,
            }),
        };
        putReq.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        var putRsp = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.OK, putRsp.StatusCode);

        // ★ THE DISCRIMINATOR. The row beginning on the Danish day is IN FORCE, so OQ-6 carry-forward
        //   — which applies only to a change that has NOT happened yet — must not have touched it.
        Assert.Equal(
            "Specialkonsulent",
            await ScalarStringAsync(
                "SELECT position FROM employee_profiles WHERE employee_id = @p0 AND effective_from = @p1",
                employeeId, DanishSummerDay));

        var body = await putRsp.Content.ReadFromJsonAsync<JsonElement>();

        // The 200 body is the state AS OF today (TASK-13810). On the Danish day that is the 16 July
        // row; on the UTC day it would be the row this request just wrote for 5–16 July.
        Assert.Equal("Specialkonsulent", body.GetProperty("position").GetString());

        // ...and nothing is scheduled any more, because 16 July has arrived.
        Assert.True(
            !body.TryGetProperty("scheduled", out var scheduled) || scheduled.ValueKind == JsonValueKind.Null,
            "Expected no scheduled change: the 16 July row is in force on the Copenhagen day, " +
            "so only a UTC-day 'today' could still call it scheduled.");
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Census row 17 — the employment-history read
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The history screen reports the DANISH day and labels a row that starts today as
    /// <c>CURRENT</c>, not <c>SCHEDULED</c>.
    ///
    /// <para>This response carries its own <c>today</c> field, so the calendar under test is
    /// asserted directly rather than inferred. <b>RED on the pre-S142 code</b>: <c>today</c> would
    /// read <c>2026-07-15</c>, the 16 July interval would be labelled <c>SCHEDULED</c> (its
    /// <c>effectiveFrom &gt; today</c>) and the interval that ENDS on 16 July would still be
    /// labelled <c>CURRENT</c> — i.e. HR opening the screen at 00:30 is told a change that took
    /// effect half an hour ago has not happened yet, and that a period that has ended is the current
    /// one.</para>
    /// </summary>
    [Fact]
    public async Task History_AfterDanishMidnight_ReportsTheDanishDay_AndLabelsTodaysRowCurrent()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        using var client = Client(host, HrToken(OrgA));

        var employeeId = NextId("cph_hist");
        await SeedSplitTimelineAsync(employeeId, DanishSummerDay);

        var json = await client.GetStringAsync(
            $"/api/hr/employees/{employeeId}/history?from=2026-01-01&to=2027-01-01");
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(DanishSummerDay, DateOnly.Parse(doc.RootElement.GetProperty("today").GetString()!));

        var intervals = doc.RootElement.GetProperty("profileHistory").EnumerateArray().ToList();

        var startingToday = Assert.Single(
            intervals.Where(i => DateOnly.Parse(i.GetProperty("effectiveFrom").GetString()!) == DanishSummerDay));
        Assert.Equal("CURRENT", startingToday.GetProperty("status").GetString());

        var endingToday = Assert.Single(
            intervals.Where(i =>
                i.GetProperty("effectiveTo").ValueKind != JsonValueKind.Null
                && DateOnly.Parse(i.GetProperty("effectiveTo").GetString()!) == DanishSummerDay));
        Assert.Equal("PAST", endingToday.GetProperty("status").GetString());
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Census row 16 — the soft-delete close stamp (a STORED date, and an event field)
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A soft-delete after Danish midnight closes the profile on the DANISH day, and the event that
    /// announces it names the same day.
    ///
    /// <para>This is the winter instant, so it also pins the CET (+01:00) offset; the marquee above
    /// pins CEST (+02:00). <b>RED on the pre-S142 code</b>: both the row's <c>effective_to</c> and
    /// the <c>EmployeeProfileSoftDeleted</c> event's <c>effectiveTo</c> would be
    /// <c>2026-01-15</c>. Under end-exclusive <c>[from, to)</c> semantics (ADR-018 D9) that makes the
    /// profile invisible for a day on which it was still in force — every as-of read downstream then
    /// believes the employee had no profile on 15 January.</para>
    ///
    /// <para>Asserting BOTH the row and the event is the point, not belt-and-braces: ADR-018 D3 says
    /// the write and its event commit together, and a close date that disagreed with the event
    /// describing it would be an audit-trail contradiction rather than a rounding detail.</para>
    /// </summary>
    [Fact]
    public async Task Delete_AfterDanishMidnight_ClosesTheProfileOnTheDanishDay_AndSaysSoInTheEvent()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.WinterEveningAlreadyTomorrowInCopenhagen);
        using var client = Client(host, GlobalAdminToken());

        var employeeId = NextId("cph_del");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);

        var getRsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);

        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq.Headers.TryAddWithoutValidation("If-Match", getRsp.Headers.ETag!.Tag);
        var delRsp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);

        Assert.Equal(
            DanishWinterDay,
            (DateOnly)(await ScalarAsync(
                "SELECT effective_to FROM employee_profiles WHERE employee_id = @p0", employeeId))!);

        var payload = await ScalarStringAsync(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @p0 AND event_type = 'EmployeeProfileSoftDeleted'
            ORDER BY outbox_id DESC LIMIT 1
            """, $"employee-profile-{employeeId}");
        using var payloadDoc = JsonDocument.Parse(payload);
        Assert.Equal(
            DanishWinterDay,
            DateOnly.Parse(payloadDoc.RootElement.GetProperty("effectiveTo").GetString()!));
    }

    /// <summary>
    /// The same soft-delete, at a MONTH boundary: a delete at 00:30 Danish time on 1 August closes
    /// the profile in AUGUST, not on 31 July.
    ///
    /// <para><b>Why this is a separate fact rather than a second assertion.</b> The day-level fact
    /// above proves the calendar moved; this one proves what the move is WORTH. Every export,
    /// settlement and approval period in StatsTid is bounded by month, so a close date on the wrong
    /// side of a month boundary does not merely name the wrong day — it puts the employee's last
    /// covered day in the wrong PAYROLL PERIOD. <b>RED on the pre-S142 code</b>: <c>2026-07-31</c>.</para>
    ///
    /// <para>The instant is local to this class: the three shared <see cref="BoundaryInstants"/> are
    /// mid-month by design (they were chosen to discriminate the offset, not the period) and so none
    /// of them can express this.</para>
    /// </summary>
    [Fact]
    public async Task Delete_AtADanishMonthBoundary_ClosesTheProfileInTheNewMonth()
    {
        using var host = _factory.WithFixedInstant(SummerMonthEndAlreadyNextMonthInCopenhagen);
        using var client = Client(host, GlobalAdminToken());

        var employeeId = NextId("cph_delmonth");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);

        var getRsp = await client.GetAsync($"/api/admin/employee-profiles/{employeeId}");
        Assert.Equal(HttpStatusCode.OK, getRsp.StatusCode);

        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq.Headers.TryAddWithoutValidation("If-Match", getRsp.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delReq)).StatusCode);

        var closedOn = (DateOnly)(await ScalarAsync(
            "SELECT effective_to FROM employee_profiles WHERE employee_id = @p0", employeeId))!;
        Assert.Equal(DanishNextMonthDay, closedOn);
        // Stated separately so a failure says WHICH thing went wrong: the wrong month is the
        // payroll-visible consequence, the wrong day is only its cause.
        Assert.Equal(8, closedOn.Month);
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Census row 21 — the entitlement-eligibility stamp
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An eligibility grant made after Danish midnight is stamped as starting on the DANISH day.
    ///
    /// <para>The stamp is server-side and forward-only (ADR-023 D8), and the absence POST gates on
    /// <c>absence.Date &gt;= effective_from</c> — so the date is not a label, it is the first day the
    /// grant lets the employee register. <b>RED on the pre-S142 code</b>: both the row and the
    /// <c>EmployeeEntitlementEligibilitySet</c> event would carry <c>2026-07-15</c>, i.e. HR granting
    /// at 00:30 would silently have granted from a day they did not choose.</para>
    ///
    /// <para>This endpoint had NO <c>TimeProvider</c> before S142 — it read the ambient
    /// <c>DateTime.UtcNow</c>, which no test clock can reach. This fact is therefore also the proof
    /// that the seam conversion actually landed: on the unconverted handler the pinned host would be
    /// ignored and the assertion would see the real wall-clock date.</para>
    /// </summary>
    [Fact]
    public async Task Eligibility_AfterDanishMidnight_StampsEffectiveFromOnTheDanishDay()
    {
        using var host = _factory.WithFixedInstant(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        using var client = Client(host, HrToken(OrgA));

        var employeeId = NextId("cph_elig");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgA);

        var req = new HttpRequestMessage(
            HttpMethod.Put, $"/api/admin/employees/{employeeId}/entitlement-eligibility/CHILD_SICK")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { eligible = true }), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        Assert.Equal(
            DanishSummerDay,
            (DateOnly)(await ScalarAsync(
                """
                SELECT effective_from FROM employee_entitlement_eligibility
                WHERE employee_id = @p0 AND entitlement_type = 'CHILD_SICK' AND effective_to IS NULL
                """, employeeId))!);

        var payload = await ScalarStringAsync(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @p0 AND event_type = 'EmployeeEntitlementEligibilitySet'
            ORDER BY outbox_id DESC LIMIT 1
            """, $"employee-entitlement-eligibility-{employeeId}-CHILD_SICK");
        using var payloadDoc = JsonDocument.Parse(payload);
        Assert.Equal(
            DanishSummerDay,
            DateOnly.Parse(payloadDoc.RootElement.GetProperty("effectiveFrom").GetString()!));
    }

    // ════════════════════════════════════════════════════════════════════════════
    // Census row 47 — EmployeeProfileRepository.CreateAsync's effective_from stamp
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The repository's profile-create stamps the new row's <c>effective_from</c> on the DANISH day.
    ///
    /// <para>Exercised repository-direct because <c>CreateAsync</c> has no production caller today
    /// (the boot seeder and the admin user-create POST both INSERT inline) — a fact reported to the
    /// Orchestrator rather than acted on, since deleting a method is not this task's call. The stamp
    /// is still the first day a profile is in force, so the calendar has to be right for any future
    /// caller. <b>RED on the pre-S142 code</b>: <c>2026-01-15</c>.</para>
    ///
    /// <para>No host is booted: the repository is constructed directly against the container with a
    /// <see cref="FixedTimeProvider"/>, which is the same seam DI supplies in production.</para>
    /// </summary>
    [Fact]
    public async Task RepositoryCreate_AfterDanishMidnight_StampsEffectiveFromOnTheDanishDay()
    {
        var employeeId = NextId("cph_create");
        await SeedUserWithoutProfileAsync(employeeId);

        var repo = new EmployeeProfileRepository(
            _harness.Factory,
            new FixedTimeProvider(BoundaryInstants.WinterEveningAlreadyTomorrowInCopenhagen));

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await repo.CreateAsync(conn, tx, new EmployeeProfileCreateRequest(
                EmployeeId: employeeId, PartTimeFraction: 1.000m, Position: null));
            await tx.CommitAsync();
        }

        Assert.Equal(
            DanishWinterDay,
            (DateOnly)(await ScalarAsync(
                "SELECT effective_from FROM employee_profiles WHERE employee_id = @p0", employeeId))!);
    }

    // ─────────────────────────────── fixtures ───────────────────────────────

    /// <summary>
    /// Seeds an employee whose profile timeline is SPLIT on <paramref name="splitDate"/>: the
    /// original row ends there ("Fuldmægtig") and a second row begins there ("Specialkonsulent").
    /// Written by direct SQL rather than through the product so the shape is stated, not inferred
    /// from another endpoint's behaviour — and so the split date is a literal the fact controls.
    /// </summary>
    private async Task SeedSplitTimelineAsync(string employeeId, DateOnly splitDate)
    {
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgA, position: "Fuldmægtig");
        await ExecAsync(
            "UPDATE employee_profiles SET effective_to = @p1 WHERE employee_id = @p0 AND effective_to IS NULL",
            employeeId, splitDate);
        await ExecAsync(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (
                gen_random_uuid(), @p0, 1.000, 'Specialkonsulent', @p1, NULL,
                (SELECT COALESCE(MAX(version), 0) + 1 FROM employee_profiles WHERE employee_id = @p0),
                (SELECT u.employment_category FROM users u WHERE u.user_id = @p0))
            """, employeeId, splitDate);
    }

    /// <summary>
    /// A users row (and its org) with NO <c>employee_profiles</c> row — the precondition
    /// <c>CreateAsync</c> exists for. Seeded directly, and never through a booted host, so the
    /// startup profile seeder cannot backfill away the absence this fact depends on.
    /// </summary>
    private async Task SeedUserWithoutProfileAsync(string employeeId)
    {
        await ExecAsync(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                       materialized_path, agreement_code, ok_version)
            VALUES (@p0, 'S142 CPH Org', 'ORGANISATION', NULL, '/STY_S142_CPH/', 'AC', 'OK24')
            ON CONFLICT (org_id) DO NOTHING
            """, OrgA);
        await ExecAsync(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version)
            VALUES (@p0, @p0, 'dev-only', @p0, NULL, @p1, 'AC', 'OK24')
            ON CONFLICT (user_id) DO NOTHING
            """, employeeId, OrgA);
    }

    // ─────────────────────────────── host + HTTP helpers ───────────────────────────────

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
        employeeId: "hr_s142_cph_actor", name: "hr_s142_cph_actor", role: StatsTidRoles.LocalHR,
        agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });

    private static string GlobalAdminToken() => NewTokenService().GenerateToken(
        employeeId: "hr_s142_cph_admin", name: "hr_s142_cph_admin", role: StatsTidRoles.GlobalAdmin,
        agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    // ─────────────────────────────── raw DB helpers ───────────────────────────────

    private async Task<string> ScalarStringAsync(string sql, params object[] args)
        => (string)(await ScalarAsync(sql, args))!;

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // compile-time test constant; values bound via parameters below
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
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
