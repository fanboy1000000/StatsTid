using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S139 / TASK-13906 — the FALSIFIABILITY PROBE for <see cref="StatsTidWebApplicationFactory.WithFixedToday"/>
/// and <see cref="FixedTimeProvider"/>.
///
/// <para>
/// <b>What this suite is about, in plain language.</b> A fixture that silently does nothing is
/// worse than none: it is not enough that <c>WithFixedToday</c> COMPILES and REGISTERS a
/// <see cref="TimeProvider"/> singleton — this suite proves the pinned clock actually REACHES the
/// product's today-dependent decisions at the HTTP surface. Legs 1-4 (this class's ORIGINAL S139
/// legs, ending at <see cref="PlainHost_TimeProviderResolvesToSystemDefault"/>) were written FROM
/// THE S139/TASK-13907 conversion SPEC, before observing whether that parallel conversion had
/// landed, so THOSE FOUR were RED-FIRST against the then-unconverted product and went GREEN once
/// TASK-13907 converted the paths they exercise: the profile future-dating guard
/// (<c>EmployeeProfileEndpoints.cs:271-273</c> + <c>EmployeeProfileRepository.cs:375/502/689</c>),
/// the dedicated agreement-code PUT's twin guard (<c>AdminEndpoints.cs:2463</c> +
/// <c>UserAgreementCodeRepository.cs:248</c>), and the profile soft-delete close-stamp
/// (<c>EmployeeProfileRepository.cs:816</c>'s SQL-side <c>NOW()::date</c> +
/// <c>EmployeeProfileEndpoints.cs:945</c>'s C#-side wall-clock (UtcNow) event construction — TWO
/// independent clock reads that must BOTH move to the seam for Leg 3 to agree with itself). Legs
/// 5-8 below are a SECOND generation, added later in S140 (TASK-14002/QUAL-153-154, then
/// TASK-14009) for seams that were ALREADY converted by the time these legs were written — they
/// were never run against an unconverted product, so "RED-FIRST" above does not describe them;
/// each states its own RED condition as REASONED from the product/repository source instead, and
/// each is first executed, RED or GREEN, only in the S140 close's watched CI job — Docker is
/// unavailable on the authoring machine (standing project constraint), so none of Legs 5-8 has run
/// at all locally.
/// </para>
///
/// <para>
/// <b>The pinned date, <see cref="F"/>.</b> 2025-03-12 — a WEDNESDAY (so it can never coincide with
/// the S138 zero-norm-weekend trap this whole task exists to retire) on the OK24 side of the
/// 2026-04-01 OK24→OK26 cutover (<c>OkVersionResolver.cs:18-19</c>), so no test here accidentally
/// exercises the version-transition edge case, which is out of this probe's scope. Both facts are
/// asserted exactly once, by <see cref="FixedToday_IsAWednesdayOnTheOk24SideOfTheOk26Cutover"/>.
/// Every date used anywhere below is DERIVED from <see cref="F"/> — never from a raw read of the
/// wall clock (no UtcNow, no Today, no DateOnly conversion of either), because a
/// probe that itself reads the wall clock could not tell a converted product from an unconverted
/// one on the very day it happens to run.
/// </para>
///
/// <para>
/// <b>Fixture-employee seeding route.</b> Every Docker-gated test below seeds its employee by
/// DIRECT SQL INSERT (mirroring <c>ProfileBackdatingEndpointTests.SeedEmployeeAsync</c> +
/// <c>ReplaceProfileTimelineAsync</c> / <c>AgreementCodeBackdatingEndpointTests.SeedUserAsync</c>),
/// NOT via the admin user-CREATE POST — deliberately, even though the task's spec offers that as an
/// alternative. <c>AdminEndpoints.cs:927</c>'s hire-date default ("omitted ⇒ today") is ITSELF one of
/// the sites TASK-13907 converts; seeding through it would make a Leg's PASS/FAIL depend on TWO
/// conversions at once (the create-time default AND the leg's own named conversion point) and could
/// misattribute a create-endpoint gap as a profile/agreement-code gap. Direct SQL insert with an
/// explicit hire date keeps each leg's RED condition attributable to exactly the one conversion point
/// it names. Every seeded hire date is <see cref="F"/> minus a fixed offset, safely on or before
/// <see cref="F"/> per <c>EmployeeProfileRepository.cs:507</c>'s
/// <c>EffectiveFrom &lt; employment_start_date</c> refusal.
/// </para>
///
/// <para>
/// <b>Boot order (PAT-008 / S63 lesson).</b> Every test below calls
/// <c>_factory.WithFixedToday(F).CreateClient()</c> FIRST, and seeds its fixture employee only
/// AFTER that call returns — the derived host's first <c>CreateClient()</c> re-runs
/// <c>Program.cs</c>'s startup seeders, which would otherwise repair/collide with a direct-INSERT
/// fixture created too early. See <see cref="StatsTidWebApplicationFactory.WithFixedToday"/>.
/// </para>
///
/// <para>
/// <b>The OMITTED "served-today" candidate — NOT the S140 Leg 5 below.</b> (Renamed off "Leg 5" here
/// because the file later grew an ACTUAL Leg 5,
/// <see cref="DelegationExpirySweep_UntilDateEqualsFixedToday_Survives_UntilDateYesterday_Closes"/>,
/// which pins the delegation-expiry sweep's inclusive boundary and has nothing to do with this
/// omitted candidate — the two must not be confused.) The S139 task spec offered a leg only if some
/// cheap endpoint on the converted paths echoes "today" as a byte-pinnable value (precedent:
/// <c>Contracts/S120BalanceSpecRuntimeTests.cs:334</c>). Reading <c>EmployeeProfileEndpoints.cs</c>
/// end to end: the PUT/GET response bodies are DELIBERATELY the state AS-OF-TODAY's business fields
/// (fraction/position/category), never a literal date, and the 422 bodies are DATE-FREE BY DESIGN
/// (ADR-040 D7 — the future-dating refusal and the employment-start refusal share one shape
/// specifically so a client cannot probe them apart). There is no field here that echoes "today" the
/// way the year-overview endpoint's <c>today</c> does. Manufacturing an indirect oracle (e.g. seeding
/// a second profile row and reading which one's values come back) would test a different mechanism
/// than "does today move" and add coupling this probe does not need. Skipped — this paragraph is the
/// "say so."
/// </para>
///
/// <para>
/// <b>Conventions mirror <c>ProfileBackdatingEndpointTests</c> and
/// <c>AgreementCodeBackdatingEndpointTests</c>:</b> a per-test container (this class implements
/// <see cref="IAsyncLifetime"/>, so xunit gives every <c>[Fact]</c> its own instance and therefore its
/// own <see cref="TestFixtures.DockerHarness"/>), a GlobalAdmin token, direct DB seeding, and direct
/// DB/outbox read-back for verification.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FixedClockProbeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";

    /// <summary>
    /// The ONE pinned "today" for every test in this class. 2025-03-12 — see the class doc for why
    /// this exact date (a Wednesday, safely on the OK24 side of the OK24→OK26 cutover).
    /// </summary>
    private static readonly DateOnly F = new(2025, 3, 12);

    /// <summary>The OK24→OK26 cutover date (<c>OkVersionResolver.cs:18-19</c>) — <see cref="F"/>
    /// must sit strictly before it so this probe never touches the version-transition edge case.</summary>
    private static readonly DateOnly Ok26CutoverDate = new(2026, 4, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // 0. The pinned constant itself — asserted ONCE for the whole class.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Locks the two facts every other test in this file leans on without re-deriving them:
    /// <see cref="F"/> is a WEDNESDAY (so no leg here can land on the S138 zero-norm-weekend trap —
    /// not that any leg below seeds an absence, but the sibling suites' <c>OnWeekday</c> convention
    /// this file otherwise mirrors exists for exactly that reason), and <see cref="F"/> sits on the
    /// OK24 side of the OK24→OK26 cutover, so nothing below exercises the version-transition edge
    /// case that is out of scope for a clock-plumbing probe.
    /// </summary>
    [Fact]
    public void FixedToday_IsAWednesdayOnTheOk24SideOfTheOk26Cutover()
    {
        Assert.Equal(DayOfWeek.Wednesday, F.DayOfWeek);
        Assert.True(F < Ok26CutoverDate,
            $"F={F:yyyy-MM-dd} must sit strictly before the OK24->OK26 cutover " +
            $"{Ok26CutoverDate:yyyy-MM-dd} (OkVersionResolver.cs:18-19).");
        // Belt-and-braces: ties F to the REAL resolver, not just a hand-copied literal.
        Assert.Equal("OK24", OkVersionResolver.ResolveVersion(F));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Leg 1 — the profile PUT's future-dating guard
    // (EmployeeProfileEndpoints.cs:271-273, EmployeeProfileRepository.cs:375/502/689)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>CORRECTED (S139 / TASK-13908 W2, Step-5a Reviewer WARNING 2).</b> This leg pins the
    /// ENDPOINT-LEVEL guard (<c>EmployeeProfileEndpoints.cs:271-273</c>) only, not the repository's
    /// own guard as the previous revision of this comment claimed.
    ///
    /// <para>
    /// <b>RED condition:</b> if the endpoint's guard still read the real wall clock instead of the
    /// injected <see cref="TimeProvider"/>, then relative to the REAL host today, F+1 (2025-03-13)
    /// is deep in the calendar PAST — an unconverted guard sees a past date, accepts it, and the
    /// first PUT below returns 200 instead of 422, failing the first assertion.
    /// </para>
    ///
    /// <para>
    /// <b>Why this leg cannot also pin the repository's guard (line 502).</b> For the F+1 request
    /// the ENDPOINT'S guard 422s FIRST — the request never reaches the repository at all. And for
    /// the F request, the repository's own "today" feeds only <c>TemporalWriteRouter.IsFutureDated</c>
    /// and the "row covering today" cache refresh, both of which answer IDENTICALLY for F and for
    /// the real wall-clock today (F is safely in the past either way) — so this leg would stay
    /// GREEN even with an unconverted repository. The repository-level guard is pinned separately,
    /// by <c>EmployeeProfileLifecycleTests.SupersedeAndCreateAsync_RepositoryGuard_FutureDated_ThrowsTemporalWriteRejectedException_ThenSameDateSucceeds</c>,
    /// which calls <c>SupersedeAndCreateAsync</c> directly with no endpoint in front of it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ProfilePut_FutureDated_Returns422_ThenSameDatePut_Returns200()
    {
        using var fixedHost = _factory.WithFixedToday(F);
        var client = fixedHost.CreateClient(); // boot the FIXED host first — PAT-008 boot order.

        var (employeeId, _) = await SeedEmployeeAsync(); // seeded AFTER the fixed host's boot.
        ApplyAdminAuth(client);

        var version = await ReadProfileVersionAsync(client, employeeId);

        var futureRsp = await PutProfileAsync(
            client, employeeId, F.AddDays(1),
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, futureRsp.StatusCode);

        // Nothing was written by the refused future-dated request, so the same version still
        // applies for the same-date write below.
        var todayRsp = await PutProfileAsync(
            client, employeeId, F,
            partTimeFraction: 0.500m, position: null, employmentCategory: null,
            ifMatch: $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, todayRsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Leg 2 — the dedicated agreement-code PUT's twin future-dating guard
    // (AdminEndpoints.cs:2463, UserAgreementCodeRepository.cs:248)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The agreement-code twin of Leg 1, on <c>PUT /api/admin/users/{userId}/agreement-code</c>.
    ///
    /// <para>
    /// <b>CORRECTED (S139 / TASK-13908 W2, Step-5a Reviewer WARNING 2).</b> This leg pins the
    /// ENDPOINT-LEVEL guard (<c>AdminEndpoints.cs:2463</c>) only, not the repository's own guard as
    /// the previous revision of this comment claimed.
    /// </para>
    ///
    /// <para>
    /// <b>RED condition:</b> if the endpoint's guard still reads the real wall clock, F+1 reads as
    /// a past date relative to the real host today, the first PUT returns 200 instead of 422, and
    /// the first assertion fails.
    /// </para>
    ///
    /// <para>
    /// <b>Why this leg cannot also pin the repository's guard (<c>UserAgreementCodeRepository.cs:248</c>).</b>
    /// For the F+1 request the ENDPOINT'S guard 422s FIRST — the request never reaches the
    /// repository. For the F request, the repository's own "today" feeds only
    /// <c>TemporalWriteRouter.IsFutureDated</c> and the "row covering today" cache refresh, which
    /// answer identically for F and for the real today — so this leg would stay GREEN even with an
    /// unconverted repository. The repository-level guard is pinned separately, by
    /// <c>AgreementCodeBackdatingEndpointTests.SupersedeAndCreateAsync_RepositoryGuard_FutureDated_ThrowsTemporalWriteRejectedException_ThenSameDateSucceeds</c>,
    /// which calls <c>SupersedeAndCreateAsync</c> directly with no endpoint in front of it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AgreementCodePut_FutureDated_Returns422_ThenSameDatePut_Returns200()
    {
        using var fixedHost = _factory.WithFixedToday(F);
        var client = fixedHost.CreateClient(); // boot the FIXED host first — PAT-008 boot order.

        var (employeeId, _) = await SeedEmployeeAsync(); // seeded AFTER the fixed host's boot.
        ApplyAdminAuth(client);

        var version = await ReadUsersVersionAsync(client, employeeId);

        var futureRsp = await PutAgreementCodeAsync(
            client, employeeId, "HK", F.AddDays(1), $"\"{version}\"");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, futureRsp.StatusCode);

        // Nothing was written by the refused future-dated request, so the same version still
        // applies for the same-date write below.
        var todayRsp = await PutAgreementCodeAsync(
            client, employeeId, "HK", F, $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, todayRsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Leg 3 — the soft-delete close-stamp: TWO independent clock reads that must BOTH move
    // (EmployeeProfileRepository.cs:816's SQL `NOW()::date`, EmployeeProfileEndpoints.cs:945's
    // C# wall-clock (UtcNow) event construction)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// DELETEs the fixture employee's profile and asserts the closed row's <c>effective_to</c> AND
    /// the emitted <c>EmployeeProfileSoftDeleted</c> event's <c>effectiveTo</c> BOTH equal
    /// <see cref="F"/>.
    ///
    /// <para>
    /// The row is read back BY <c>profile_id</c> (known here because THIS test generated it when
    /// seeding, and cross-checked against the event's own <c>profileId</c> field) — NEVER by
    /// <c>effective_to IS NULL</c>. A same-day create-then-close leaves a ZERO-WIDTH
    /// <c>[F, F)</c> row: once closed, <c>effective_to</c> is <see cref="F"/>, not NULL, so an
    /// <c>IS NULL</c> lookup after the DELETE would find nothing and could be misread as "no row" —
    /// exactly the failure mode this test structure avoids.
    /// </para>
    ///
    /// <para>
    /// <b>Two independent RED conditions</b> (either one alone fails this test, because the
    /// assertion is that row and event AGREE on <see cref="F"/>):
    /// </para>
    /// <list type="bullet">
    ///   <item>If <c>EmployeeProfileRepository.cs:816</c> still stamps <c>effective_to = NOW()::date</c>
    ///     (the Postgres SQL-side wall clock, untouched by the app-level <see cref="TimeProvider"/>
    ///     override no matter what), the ROW carries the REAL today while the EVENT (once converted)
    ///     carries <see cref="F"/> — they disagree.</item>
    ///   <item>If <c>EmployeeProfileEndpoints.cs:945</c> still constructs its
    ///     <c>EffectiveTo</c> from a raw <c>DateOnly.FromDateTime</c> read of the wall clock's UtcNow
    ///     instant (the C#-side wall clock) while
    ///     the repository HAS been converted to stamp <see cref="F"/>, the ROW carries
    ///     <see cref="F"/> while the EVENT carries the REAL today — they disagree the other way.</item>
    /// </list>
    /// Only when BOTH sites read the injected <see cref="TimeProvider"/> do row and event agree on
    /// <see cref="F"/>, which is the only value this test accepts.
    /// </summary>
    [Fact]
    public async Task SoftDelete_RowEffectiveToAndEventEffectiveTo_BothEqualFixedToday()
    {
        using var fixedHost = _factory.WithFixedToday(F);
        var client = fixedHost.CreateClient(); // boot the FIXED host first — PAT-008 boot order.

        var (employeeId, profileId) = await SeedEmployeeAsync(); // seeded AFTER the fixed host's boot.
        ApplyAdminAuth(client);

        var version = await ReadProfileVersionAsync(client, employeeId);

        var delReq = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/employee-profiles/{employeeId}");
        delReq.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var delRsp = await client.SendAsync(delReq);
        Assert.Equal(HttpStatusCode.NoContent, delRsp.StatusCode);

        // Read the event back on the per-employee stream — carries the predecessor's profile_id.
        var rawPayload = await ReadLatestEventPayloadAsync(
            $"employee-profile-{employeeId}", "EmployeeProfileSoftDeleted");
        Assert.NotNull(rawPayload);
        using var payloadDoc = JsonDocument.Parse(rawPayload!);
        Assert.Equal(profileId, payloadDoc.RootElement.GetProperty("profileId").GetGuid());
        var eventEffectiveTo = payloadDoc.RootElement.GetProperty("effectiveTo").GetString();
        Assert.Equal(F.ToString("yyyy-MM-dd"), eventEffectiveTo);

        // Read the row back BY profile_id — never by `effective_to IS NULL` (see method doc).
        var rowEffectiveTo = await ReadProfileEffectiveToByIdAsync(profileId);
        Assert.Equal(F, rowEffectiveTo);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Leg 4 — the default is unchanged: a PLAIN host still resolves TimeProvider.System
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A DI fact, not a real-calendar comparison: a host that never called
    /// <see cref="StatsTidWebApplicationFactory.WithFixedToday"/> must still resolve the SAME
    /// singleton instance Program.cs:395 registers (<c>TimeProvider.System</c>) — proving
    /// <c>WithFixedToday</c> is a per-test opt-in, not a change to the production default.
    /// Deliberately never compares against a raw read of the wall clock's UtcNow instant: the point is object identity of
    /// the DI registration, which is wall-clock-independent by construction.
    /// </summary>
    [Fact]
    public void PlainHost_TimeProviderResolvesToSystemDefault()
    {
        var resolved = _factory.Services.GetRequiredService<TimeProvider>();
        Assert.Same(TimeProvider.System, resolved);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Leg 5 — S140 / TASK-14002 (QUAL-153/154): the delegation-expiry sweep's inclusive
    // "til og med" boundary (DelegationExpiryService.cs — the sweep's SQL now binds @today
    // off the injected TimeProvider instead of reading the database's CURRENT_DATE).
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Plants TWO <c>manager_vikar</c> rows that differ ONLY in <c>until_date</c> — one at
    /// <see cref="F"/> (the R4a inclusive boundary: still "til og med" covered today), one at
    /// <c>F − 1</c> (expired the day after it) — then runs ONE sweep pass with the fixed clock
    /// and asserts the F row SURVIVES while the F-1 row CLOSES.
    ///
    /// <para>
    /// <b>RED condition — the leg the real clock cannot pass.</b> <see cref="F"/> is 2025-03-12,
    /// well over a year before this task was authored. Under the REAL wall clock (swap the
    /// <c>timeProvider: new FixedTimeProvider(F)</c> argument below for none, so the service falls
    /// back to <see cref="TimeProvider.System"/>) BOTH <c>until_date</c>s are long in the past —
    /// the sweep would close BOTH rows, and the first assertion
    /// (<c>GetActiveByApproverAnyDateAsync</c> for the F row returning non-null) would fail. This
    /// condition is REASONED from the sweep's SQL and constructor (cited above), NOT executed on
    /// the authoring machine — Docker is unavailable there (standing project constraint), so this
    /// leg has not been run at all locally. It first runs, RED or GREEN, in the S140 close's
    /// watched CI job — no anchor nudged onto a weekday would fix a failure there, because the
    /// defect this leg targets is the SWEEP'S CLOCK SOURCE, not a day-of-week norm.
    /// </para>
    ///
    /// <para>
    /// This leg constructs <see cref="DelegationExpiryService"/> directly (no HTTP endpoint calls
    /// this sweep) — the boot below exists only for the PAT-008 boot-order rule ahead of the
    /// direct-SQL seeding that follows it, mirroring <see cref="ManagerVikarEngineTests"/>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DelegationExpirySweep_UntilDateEqualsFixedToday_Survives_UntilDateYesterday_Closes()
    {
        using var fixedHost = _factory.WithFixedToday(F);
        _ = fixedHost.CreateClient(); // boot the FIXED host first — PAT-008 boot order.

        // Two independent approver/vikar pairs (manager_vikar's partial-unique index allows only
        // one ACTIVE row per absent_approver_id, so two rows need two distinct approvers) —
        // seeded AFTER the fixed host's boot.
        var (approverStillActive, vikarStillActive) = await SeedVikarPairUsersAsync("stillactive");
        var (approverExpired, vikarExpired) = await SeedVikarPairUsersAsync("expired");

        var dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        var vikarRepo = new ManagerVikarRepository(dbFactory);

        var stillActiveId = await PlantVikarRowAsync(dbFactory, vikarRepo, approverStillActive, vikarStillActive, F);
        var expiredId = await PlantVikarRowAsync(dbFactory, vikarRepo, approverExpired, vikarExpired, F.AddDays(-1));

        var realOutbox = new PostgresEventStore(dbFactory, new OutboxServiceContext("backend-api"));
        var auditRepo = new AuditProjectionRepository(dbFactory);
        var endedMapper = new ManagerVikarEndedAuditMapper();
        var service = new DelegationExpiryService(
            dbFactory, realOutbox, vikarRepo, auditRepo, endedMapper,
            NullLogger<DelegationExpiryService>.Instance,
            timeProvider: new FixedTimeProvider(F));
        await service.CloseExpiredDelegationsAsync(CancellationToken.None);

        var survivor = await vikarRepo.GetActiveByApproverAnyDateAsync(approverStillActive);
        Assert.NotNull(survivor);
        Assert.Equal(stillActiveId, survivor!.VikarId);
        Assert.Null(await vikarRepo.GetActiveByApproverAnyDateAsync(approverExpired));
        _ = expiredId; // identity asserted via the null active-lookup above; kept for readability.
    }

    // ═════════════════════════════════════════════════════════════════════
    // Leg 6 — S140 / TASK-14002: the designated-approver authorizer's no-`asOf` fallback —
    // a SEAM PIN ON A PRODUCTION-DEAD FALLBACK (DesignatedApproverAuthorizer.cs — every
    // endpoint caller passes an explicit `asOf`, so this guards only the test-only direct-
    // construction path, never a live decision).
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A vikar effective through <see cref="F"/> (<c>UntilDate = F + 1</c> — <c>manager_vikar</c>
    /// stores no start date, so the plan's conceptual window "[F − 1, F + 1]" is realized as: the
    /// row exists from creation and covers <see cref="F"/> under the inclusive "til og med"
    /// boundary) must resolve as the SINGLE effective approver through
    /// <see cref="DesignatedApproverAuthorizer.IsEffectiveDesignatedApproverAsync(string, string, DateOnly?, CancellationToken)"/>'s
    /// no-<c>asOf</c> overload — i.e. called WITHOUT an explicit date, so the authorizer's
    /// <c>asOf ?? ctx?.AsOf ?? &lt;injected clock&gt;</c> fallback is what actually resolves "today".
    ///
    /// <para>
    /// <b>Labelled a seam pin on a production-dead fallback.</b> Every real endpoint caller passes
    /// <c>asOf: today</c> explicitly (<c>ApprovalEndpoints.cs:265,327,476,508,1217,1562,1593</c>;
    /// <c>ComplianceEndpoints.cs:80</c>; <c>SkemaEndpoints.cs:219</c>), so this fallback is reached
    /// ONLY by a test that constructs <see cref="DesignatedApproverAuthorizer"/> directly and omits
    /// <c>asOf</c> — this leg exists to prove that path is wired, not to prove any production
    /// behaviour changed.
    /// </para>
    ///
    /// <para>
    /// <b>RED condition.</b> If <see cref="DesignatedApproverAuthorizer"/>'s constructor dropped
    /// (or ignored) the <c>timeProvider</c> argument, the fallback would read
    /// <see cref="TimeProvider.System"/> — the real wall clock, over a year past
    /// <c>UntilDate = F + 1</c> (2025-03-13). The resolver would then see the vikar as expired,
    /// fall through to the absent manager (or escalate further), and the resolved single winner
    /// would never equal the vikar — <c>IsEffectiveDesignatedApproverAsync</c> would return
    /// <c>false</c>, failing <c>Assert.True</c> below.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Authorizer_NoAsOfFallback_ResolvesVikarEffectiveThroughFixedToday()
    {
        using var fixedHost = _factory.WithFixedToday(F);
        _ = fixedHost.CreateClient(); // boot the FIXED host first — PAT-008 boot order; no HTTP call follows.

        var (managerId, employeeId, vikarUserId) = await SeedManagerEmployeeVikarUsersAsync("authfallback");

        var dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        var vikarRepo = new ManagerVikarRepository(dbFactory);
        await using (var conn = dbFactory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await vikarRepo.CreateAsync(conn, tx, new ManagerVikar
            {
                VikarId = Guid.NewGuid(),
                AbsentApproverId = managerId,
                VikarUserId = vikarUserId,
                UntilDate = F.AddDays(1), // "til og med" F+1 — covers F under the inclusive bound.
                Reason = "ANDET",
                OrganisationId = OrgId,
                Version = 1,
                CreatedBy = "TEST",
            });
            await tx.CommitAsync();
        }

        var rlRepo = new ReportingLineRepository(dbFactory, vikarRepo, timeProvider: new FixedTimeProvider(F));
        var authorizer = new DesignatedApproverAuthorizer(dbFactory, rlRepo, timeProvider: new FixedTimeProvider(F));

        // NO asOf supplied — exercises the fallback, not an explicit date.
        var isEffective = await authorizer.IsEffectiveDesignatedApproverAsync(vikarUserId, employeeId);
        Assert.True(isEffective,
            "The vikar (effective through F under the fixed clock's no-asOf fallback) must be the " +
            "single effective approver — a false verdict means the fallback read the real wall " +
            "clock instead of the injected FixedTimeProvider.");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Leg 7 — S140 / TASK-14002: the admin-vikar POST echoes effectiveFrom = F
    // (ReportingLineEndpoints.cs:2232-2234 — the response's "today" now comes off the
    // injected TimeProvider, read ONCE and reused for the effectiveTo>today validation
    // and the echoed effectiveFrom, PAT-028).
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>POST /api/admin/reporting-lines/{managerId}/vikar</c>'s response <c>effectiveFrom</c>
    /// must equal <see cref="F"/> — proving the fixed clock reaches this handler, not merely the
    /// repositories it calls.
    ///
    /// <para>
    /// <b>RED condition.</b> If <c>ReportingLineEndpoints.cs:2232-2234</c> still read the wall
    /// clock's UtcNow instant directly instead of the injected <see cref="TimeProvider"/>, this
    /// would compare the literal <see cref="F"/> (2025-03-12) against whatever the REAL wall-clock
    /// date is when the suite runs — never equal outside one specific calendar day — and fail.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdminVikarPost_EchoesEffectiveFrom_EqualToFixedToday()
    {
        using var fixedHost = _factory.WithFixedToday(F);
        var client = fixedHost.CreateClient(); // boot the FIXED host first — PAT-008 boot order.
        ApplyAdminAuth(client);

        // Seeded AFTER the fixed host's boot.
        var (managerId, _, vikarUserId) = await SeedManagerEmployeeVikarUsersAsync("adminpost");

        var rsp = await client.PostAsJsonAsync(
            $"/api/admin/reporting-lines/{managerId}/vikar",
            new { vikarUserId, effectiveTo = F.AddDays(30).ToString("yyyy-MM-dd") });
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(F.ToString("yyyy-MM-dd"), body.GetProperty("effectiveFrom").GetString());
    }

    // ═════════════════════════════════════════════════════════════════════
    // Leg 8 — S140 / TASK-14009: the cross-organisation TRANSFER fan-out's shared business
    // date (AdminEndpoints.cs — PUT /api/admin/users/{userId}, the `today` read at :2249 that
    // reaches FOUR writes: the closed reporting line's effective_to, the emitted
    // ReportingLineSuperseded.EffectiveTo, the closed manager_vikar row, and each
    // ManagerVikarEnded.EffectiveTo). The users row's `updated_at` audit timestamp stays on the
    // real clock BY DESIGN and is deliberately NOT asserted against F here.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An admin PUT that moves an employee to a different Organisation closes TWO kinds of
    /// cross-Organisation-forbidden edges the transferred user held — their own reporting-line
    /// PRIMARY edge, and (a second write through the SAME shared date) an active
    /// <c>manager_vikar</c> row naming them as the stand-in — and both closures, plus their
    /// emitted events, must carry <see cref="F"/>, never the real transfer-time day.
    ///
    /// <para>
    /// <b>Why one leg covers both writes.</b> TASK-14009 moved ONE variable (<c>today</c>,
    /// <c>AdminEndpoints.cs:2249</c>) onto the injected <see cref="TimeProvider"/>; that one value
    /// feeds all four sites named above. Asserting the reporting-line row, its event, AND the
    /// vikar closure from the SAME transfer proves they trace to the one shared value — if they
    /// ever disagreed, that would be a genuine defect (two clocks reappearing), not a clock
    /// artefact this leg could paper over.
    /// </para>
    ///
    /// <para>
    /// <b>Fixture conditions this leg depends on</b> (each is load-bearing — get one wrong and the
    /// leg fails for the WRONG reason, never the clock's):
    /// </para>
    /// <list type="bullet">
    ///   <item>The request must genuinely CHANGE <c>primary_org_id</c> — the whole fan-out sits
    ///     inside <c>if (isTransfer)</c> (<c>AdminEndpoints.cs:2233</c>); a same-org PUT never
    ///     computes <c>today</c> at all.</item>
    ///   <item>The transferred employee must hold an ACTIVE reporting line as the REPORT, never as
    ///     the manager — a transferred user who still manages active reports is 422-BLOCKED before
    ///     the fan-out ever runs (<c>AdminEndpoints.cs</c>'s pre-check on
    ///     <c>GetDirectReportsAsync</c>); the emit loop over
    ///     <c>GetActiveByEmployeeInTxAsync</c> produces nothing for an employee with no incoming
    ///     edge.</item>
    ///   <item>The transferred employee must ALSO be named in an ACTIVE <c>manager_vikar</c> row
    ///     (here: as the vikar stand-in for a separate absent manager) —
    ///     <c>CloseAllInvolvingUserAsync</c> closes every row naming them by either column,
    ///     regardless of which role they hold in it.</item>
    /// </list>
    ///
    /// <para>
    /// <b>RED condition — genuine on both halves.</b> Revert <c>today</c>
    /// (<c>AdminEndpoints.cs:2249</c>) to derive from the real clock (as it did before TASK-14009
    /// pinned it to the seam) and BOTH halves fail: <see cref="F"/> is over a year before the real
    /// wall-clock day, so the reporting line's <c>effective_to</c>, the
    /// <c>ReportingLineSuperseded.EffectiveTo</c>, the closed <c>manager_vikar</c> row, and
    /// <c>ManagerVikarEnded.EffectiveTo</c> would all read the real UTC day instead of
    /// <see cref="F"/>, failing every assertion below.
    /// </para>
    ///
    /// <para>
    /// <b>CORRECTED (Step-5a cycle 2) — the earlier wording overstated this.</b> Dropping ONLY the
    /// <c>closeDate: today</c> argument at the <c>AdminEndpoints.cs</c> call site would NOT
    /// independently fail this leg. <c>ReportingLineRepository.RemoveAsync</c>'s <c>closeDate</c>
    /// parameter is OPTIONAL, and its fallback (<c>ReportingLineRepository.cs:405</c>) reads that
    /// repository's OWN injected <see cref="TimeProvider"/> — the SAME fixed provider
    /// <c>WithFixedToday</c> already pins for this host's DI container — so a caller that stopped
    /// passing <c>closeDate:</c> explicitly would still get <see cref="F"/> back from the fallback,
    /// and this half would stay GREEN. The row half's RED condition is genuine only against a FULL
    /// revert of that repository to the PRE-TASK-14001 SQL (<c>effective_to = CURRENT_DATE</c>, the
    /// database's own clock, which <c>WithFixedToday</c> cannot reach) — that is the one change
    /// this half actually disagrees with.
    /// </para>
    ///
    /// <para>
    /// This condition is REASONED from the handler's and repository's source (cited above), NOT
    /// executed on the authoring machine — Docker is unavailable there (standing project
    /// constraint) — so it first runs, RED or GREEN, in the S140 close's watched CI job.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AdminTransfer_ClosesReportingLineAndVikar_BothEffectiveToEqualFixedToday()
    {
        using var fixedHost = _factory.WithFixedToday(F);
        var client = fixedHost.CreateClient(); // PAT-008 boot order.
        ApplyAdminAuth(client);

        // Seeded AFTER the fixed host's boot.
        var (transferredUserId, managerId, _, vikarId) = await SeedTransferFixtureAsync();

        var version = await ReadUsersVersionAsync(client, transferredUserId);
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{transferredUserId}")
        {
            Content = JsonContent.Create(new { primaryOrgId = TransferNewOrg }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        var rsp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        // ── the reporting-line half ──
        var rowEffectiveTo = await ReadReportingLineEffectiveToAsync(transferredUserId, managerId);
        Assert.Equal(F, rowEffectiveTo);

        var supersededPayload = await ReadLatestEventPayloadAsync(
            $"reporting-line-{transferredUserId}", "ReportingLineSuperseded");
        Assert.NotNull(supersededPayload);
        using (var supersededDoc = JsonDocument.Parse(supersededPayload!))
        {
            Assert.Equal(F.ToString("yyyy-MM-dd"),
                supersededDoc.RootElement.GetProperty("effectiveTo").GetString());
        }

        // ── the vikar half — a second write through the SAME shared value ──
        var vikarEffectiveTo = await ReadManagerVikarEffectiveToAsync(vikarId);
        Assert.Equal(F, vikarEffectiveTo);

        var vikarEndedPayload = await ReadLatestEventPayloadAsync(
            $"manager-vikar-{vikarId}", "ManagerVikarEnded");
        Assert.NotNull(vikarEndedPayload);
        using (var vikarEndedDoc = JsonDocument.Parse(vikarEndedPayload!))
        {
            Assert.Equal(F.ToString("yyyy-MM-dd"),
                vikarEndedDoc.RootElement.GetProperty("effectiveTo").GetString());
        }
    }

    // ─── Seeding helpers (Legs 5-7) ────────────────────────────────────────

    /// <summary>
    /// Seeds two bare users (an absent approver + a stand-in), no roles or reporting lines — all
    /// Leg 5's sweep needs, since <c>manager_vikar</c>'s only FK requirement is that both user ids
    /// exist. Distinct per call (a fresh <paramref name="label"/> keeps ids collision-free across
    /// the two pairs one test seeds).
    /// </summary>
    private async Task<(string ApproverId, string VikarId)> SeedVikarPairUsersAsync(string label)
    {
        var approverId = $"appr_s14002_{label}_" + Guid.NewGuid().ToString("N")[..6];
        var vikarId = $"vik_s14002_{label}_" + Guid.NewGuid().ToString("N")[..6];

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@appr, @appr, 'dev-only', 'S140 Approver', NULL, @org, 'HK', 'OK24', TRUE),
                (@vik,  @vik,  'dev-only', 'S140 Vikar',    NULL, @org, 'HK', 'OK24', TRUE)
            """, conn);
        cmd.Parameters.AddWithValue("appr", approverId);
        cmd.Parameters.AddWithValue("vik", vikarId);
        cmd.Parameters.AddWithValue("org", OrgId);
        await cmd.ExecuteNonQueryAsync();

        return (approverId, vikarId);
    }

    /// <summary>Directly plants a <c>manager_vikar</c> row via the real repository (mirrors
    /// <c>ManagerVikarEngineTests.CreateVikarAsync</c>). Returns the created row's id.</summary>
    private static async Task<Guid> PlantVikarRowAsync(
        DbConnectionFactory dbFactory, ManagerVikarRepository vikarRepo,
        string absentApprover, string vikarUser, DateOnly untilDate)
    {
        await using var conn = dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var v = await vikarRepo.CreateAsync(conn, tx, new ManagerVikar
        {
            VikarId = Guid.NewGuid(),
            AbsentApproverId = absentApprover,
            VikarUserId = vikarUser,
            UntilDate = untilDate,
            Reason = "ANDET",
            OrganisationId = OrgId,
            Version = 1,
            CreatedBy = "TEST",
        });
        await tx.CommitAsync();
        return v.VikarId;
    }

    /// <summary>
    /// Seeds a manager + one PRIMARY report + a vikar-eligible LOCAL_LEADER user, all on
    /// <see cref="OrgId"/> — the minimum fixture Legs 6 and 7 share (a resolvable designated-
    /// approver edge plus a same-org, role-qualifying vikar candidate). The reporting line's
    /// <c>EffectiveFrom</c> is a year before <see cref="F"/> so it is already in force as of F under
    /// the fixed clock (mirrors the S140 fix applied to the six converted suites' own fixtures).
    /// </summary>
    private async Task<(string ManagerId, string EmployeeId, string VikarUserId)> SeedManagerEmployeeVikarUsersAsync(string label)
    {
        var managerId = $"mgr_s14002_{label}_" + Guid.NewGuid().ToString("N")[..6];
        var employeeId = $"emp_s14002_{label}_" + Guid.NewGuid().ToString("N")[..6];
        var vikarUserId = $"vik_s14002_{label}_" + Guid.NewGuid().ToString("N")[..6];

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@mgr, @mgr, 'dev-only', 'S140 Manager', NULL, @org, 'HK', 'OK24', TRUE),
                (@emp, @emp, 'dev-only', 'S140 Employee', NULL, @org, 'HK', 'OK24', TRUE),
                (@vik, @vik, 'dev-only', 'S140 VikarCandidate', NULL, @org, 'HK', 'OK24', TRUE)
            """, conn))
        {
            cmd.Parameters.AddWithValue("mgr", managerId);
            cmd.Parameters.AddWithValue("emp", employeeId);
            cmd.Parameters.AddWithValue("vik", vikarUserId);
            cmd.Parameters.AddWithValue("org", OrgId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@vik, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST')
            """, conn))
        {
            cmd.Parameters.AddWithValue("vik", vikarUserId);
            cmd.Parameters.AddWithValue("org", OrgId);
            await cmd.ExecuteNonQueryAsync();
        }

        var dbFactory = new DbConnectionFactory(_harness.ConnectionString);
        var rlRepo = new ReportingLineRepository(dbFactory);
        await rlRepo.AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = employeeId,
            ManagerId = managerId,
            OrganisationId = OrgId,
            Relationship = "PRIMARY",
            EffectiveFrom = F.AddYears(-1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });

        return (managerId, employeeId, vikarUserId);
    }

    // ─── Seeding + read helpers (Leg 8) ────────────────────────────────────

    /// <summary>The transfer's OLD Organisation — an ORGANISATION-type org (a MAO cannot hold
    /// employees), matching the proven cross-Org transfer fixture shape
    /// (<c>S104UnitManagementTests.OrgA</c>).</summary>
    private const string TransferOldOrg = "STY02";

    /// <summary>The transfer's NEW (destination) Organisation — a DIFFERENT MAO's tree
    /// (<c>S104UnitManagementTests.OrgB</c>), so the PUT genuinely changes <c>primary_org_id</c>.</summary>
    private const string TransferNewOrg = "STY05";

    /// <summary>
    /// Seeds a manager + a transferred employee reporting PRIMARY to that manager (both on
    /// <see cref="TransferOldOrg"/>), plus a SEPARATE absent manager for whom the transferred
    /// employee stands in as vikar. This is Leg 8's two load-bearing fixture conditions in one
    /// helper: an ACTIVE reporting line where the transferred employee is the REPORT (never the
    /// manager — a manager with active reports is 422-blocked before the fan-out runs), and an
    /// ACTIVE <c>manager_vikar</c> row naming the transferred employee (here, as the stand-in).
    /// No <c>units</c> rows are needed — <c>AcquireUnitOrgLockAsync</c> is a bare advisory key on
    /// the org id string, not a row lookup.
    /// </summary>
    private async Task<(string TransferredUserId, string ManagerId, string AbsentManagerId, Guid VikarId)>
        SeedTransferFixtureAsync()
    {
        var managerId = "mgr_s14009_" + Guid.NewGuid().ToString("N")[..6];
        var transferredUserId = "xfer_s14009_" + Guid.NewGuid().ToString("N")[..6];
        var absentManagerId = "absmgr_s14009_" + Guid.NewGuid().ToString("N")[..6];

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES
                (@mgr,    @mgr,    'dev-only', 'S140 Transfer Manager', NULL, @oldOrg, 'HK', 'OK24', TRUE),
                (@xfer,   @xfer,   'dev-only', 'S140 Transferred User', NULL, @oldOrg, 'HK', 'OK24', TRUE),
                (@absmgr, @absmgr, 'dev-only', 'S140 Absent Manager',   NULL, @oldOrg, 'HK', 'OK24', TRUE)
            """, conn))
        {
            cmd.Parameters.AddWithValue("mgr", managerId);
            cmd.Parameters.AddWithValue("xfer", transferredUserId);
            cmd.Parameters.AddWithValue("absmgr", absentManagerId);
            cmd.Parameters.AddWithValue("oldOrg", TransferOldOrg);
            await cmd.ExecuteNonQueryAsync();
        }

        var dbFactory = new DbConnectionFactory(_harness.ConnectionString);

        // The transferred employee's own PRIMARY edge — EffectiveFrom well before F so it is
        // already in force under the fixed clock (the S140 fix applied throughout this task).
        var rlRepo = new ReportingLineRepository(dbFactory);
        await rlRepo.AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = transferredUserId,
            ManagerId = managerId,
            OrganisationId = TransferOldOrg,
            Relationship = "PRIMARY",
            EffectiveFrom = F.AddYears(-1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });

        // The transferred employee as VIKAR STAND-IN for the separate absent manager — an active
        // row naming them, closed by the SAME transfer's fan-out (CloseAllInvolvingUserAsync).
        var vikarRepo = new ManagerVikarRepository(dbFactory);
        var vikarId = Guid.NewGuid();
        await using (var vconn = dbFactory.Create())
        {
            await vconn.OpenAsync();
            await using var tx = await vconn.BeginTransactionAsync();
            await vikarRepo.CreateAsync(vconn, tx, new ManagerVikar
            {
                VikarId = vikarId,
                AbsentApproverId = absentManagerId,
                VikarUserId = transferredUserId,
                UntilDate = F.AddDays(30),
                Reason = "ANDET",
                OrganisationId = TransferOldOrg,
                Version = 1,
                CreatedBy = "TEST",
            });
            await tx.CommitAsync();
        }

        return (transferredUserId, managerId, absentManagerId, vikarId);
    }

    /// <summary>Reads the (now-closed) reporting line's <c>effective_to</c> by its natural
    /// (employee, manager) pair — unique enough given the fresh ids each call seeds.</summary>
    private async Task<DateOnly?> ReadReportingLineEffectiveToAsync(string employeeId, string managerId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT effective_to FROM reporting_lines WHERE employee_id = @e AND manager_id = @m", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("m", managerId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(),
            $"expected a reporting_lines row for employee_id={employeeId}, manager_id={managerId}.");
        return reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0);
    }

    /// <summary>Reads the (now-closed) <c>manager_vikar</c> row's <c>effective_to</c> by its known
    /// id (generated by <see cref="SeedTransferFixtureAsync"/>, not re-derived later).</summary>
    private async Task<DateOnly?> ReadManagerVikarEffectiveToAsync(Guid vikarId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT effective_to FROM manager_vikar WHERE vikar_id = @id", conn);
        cmd.Parameters.AddWithValue("id", vikarId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"expected a manager_vikar row for vikar_id={vikarId}.");
        return reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0);
    }

    // ─── Seeding helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Seeds a user + an OPEN employee_profiles row + an OPEN user_agreement_codes row, all
    /// starting well before <see cref="F"/> (direct SQL, chosen over the admin-create POST — see
    /// the class doc's "Fixture-employee seeding route" section for why). Returns the employee id
    /// and the profile row's known <c>profile_id</c> (generated here, not re-derived later).
    /// </summary>
    private async Task<(string EmployeeId, Guid ProfileId)> SeedEmployeeAsync()
    {
        var employeeId = "emp_s13906_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var hireDate = F.AddDays(-100);
        var profileId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, employment_category,
                               employment_start_date, is_active)
            VALUES (@u, @u, 'dev-only', 'S139/TASK-13906 Fixed-Clock Probe User', NULL,
                    @org, 'AC', 'OK24', 'Standard', @hire, TRUE)
            """, conn))
        {
            userCmd.Parameters.AddWithValue("u", employeeId);
            userCmd.Parameters.AddWithValue("org", OrgId);
            userCmd.Parameters.AddWithValue("hire", hireDate);
            await userCmd.ExecuteNonQueryAsync();
        }

        await using (var profileCmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles
                (profile_id, employee_id, part_time_fraction, employment_category,
                 effective_from, effective_to, version)
            VALUES (@p, @u, 1.000, 'Standard', @hire, NULL, 1)
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("p", profileId);
            profileCmd.Parameters.AddWithValue("u", employeeId);
            profileCmd.Parameters.AddWithValue("hire", hireDate);
            await profileCmd.ExecuteNonQueryAsync();
        }

        await using (var agreementCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, 'AC', @hire, NULL, 1)
            """, conn))
        {
            agreementCmd.Parameters.AddWithValue("u", employeeId);
            agreementCmd.Parameters.AddWithValue("hire", hireDate);
            await agreementCmd.ExecuteNonQueryAsync();
        }

        return (employeeId, profileId);
    }

    // ─── Read helpers ────────────────────────────────────────────────────

    private async Task<DateOnly?> ReadProfileEffectiveToByIdAsync(Guid profileId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT effective_to FROM employee_profiles WHERE profile_id = @p", conn);
        cmd.Parameters.AddWithValue("p", profileId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"expected an employee_profiles row for profile_id={profileId}.");
        return reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0);
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

    // ─── HTTP helpers ────────────────────────────────────────────────────

    private static void ApplyAdminAuth(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken());
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
            employeeId: "ADMIN_S13906_FC",
            name: "S139/TASK-13906 Fixed-Clock Probe Admin",
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

    private static async Task<HttpResponseMessage> PutAgreementCodeAsync(
        HttpClient client, string userId, string agreementCode, DateOnly effectiveFrom, string ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{userId}/agreement-code")
        {
            Content = JsonContent.Create(new
            {
                agreementCode,
                effectiveFrom = effectiveFrom.ToString("yyyy-MM-dd"),
            }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }
}
