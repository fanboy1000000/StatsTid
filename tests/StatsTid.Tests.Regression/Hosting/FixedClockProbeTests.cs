using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S139 / TASK-13906 — the FALSIFIABILITY PROBE for <see cref="StatsTidWebApplicationFactory.WithFixedToday"/>
/// and <see cref="FixedTimeProvider"/>.
///
/// <para>
/// <b>What this suite is about, in plain language.</b> A fixture that silently does nothing is
/// worse than none: it is not enough that <c>WithFixedToday</c> COMPILES and REGISTERS a
/// <see cref="TimeProvider"/> singleton — this suite proves the pinned clock actually REACHES the
/// product's today-dependent decisions at the HTTP surface. It is written FROM THE S139/TASK-13907
/// conversion SPEC, before observing whether that parallel conversion has landed, so every test here
/// is RED-FIRST against the unconverted product and goes GREEN once TASK-13907 converts the paths it
/// exercises: the profile future-dating guard (<c>EmployeeProfileEndpoints.cs:271-273</c> +
/// <c>EmployeeProfileRepository.cs:375/502/689</c>), the dedicated agreement-code PUT's twin guard
/// (<c>AdminEndpoints.cs:2463</c> + <c>UserAgreementCodeRepository.cs:248</c>), and the profile
/// soft-delete close-stamp (<c>EmployeeProfileRepository.cs:816</c>'s SQL-side <c>NOW()::date</c> +
/// <c>EmployeeProfileEndpoints.cs:945</c>'s C#-side wall-clock (UtcNow) event construction — TWO
/// independent clock reads that must BOTH move to the seam for Leg 3 to agree with itself).
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
/// <b>Leg 5 ("served today") — DELIBERATELY OMITTED.</b> The task spec offers this leg only if some
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
