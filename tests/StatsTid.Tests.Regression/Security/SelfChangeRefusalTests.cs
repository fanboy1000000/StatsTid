using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.SharedKernel.Security;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S148 / TASK-14821-T (AC-C13h (2)-(6); P-26, A-18, NR-16) - nobody changes their own pay inputs.
///
/// <para>
/// <b>In plain language.</b> An HR user who is also a person in the system may not change their OWN
/// agreement code, hours fraction, position or employment category, and may not close their own
/// profile: those decide their own pay, so a second administrator must do it. The refusal is a 403 and
/// nothing is written. Saving their own e-mail or display name through the edit drawer, which re-sends
/// every field unchanged, still works, because nothing a pay input depends on changes.
/// </para>
///
/// <para>
/// <b>Fixture.</b> The Backend host with the clock fixed at Tuesday 10 November 2026
/// (<see cref="Clock"/>). The actor is a LocalHR scoped to STY01 whose employee id IS the subject's
/// (a person who is also an HR user, managing nobody); the AC-C13h (5) facts use a GlobalAdmin with GLOBAL scope and
/// the same employee id (the top role, so a role-gated bypass would show). The 403 reason is contract (ruling 2.6):
/// <see cref="SelfReason"/>, one string on all four sites. "Nothing written" is <see cref="WriteTrailAsync"/>
/// plus <c>users.version</c> unchanged. Docker-gated: <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SelfChangeRefusalTests : IClassFixture<DepartedSelfHostFixture>
{
    /// <summary>The P-26 reason as a literal (ruling 2.6), never the production constant.</summary>
    private const string SelfReason =
        "Own agreement code and employee profile cannot be modified; a second administrator must perform this change";

    private readonly DepartedSelfHostFixture _fx;

    public SelfChangeRefusalTests(DepartedSelfHostFixture fx) => _fx = fx;

    private string Cs => _fx.ConnectionString;

    /// <summary>A derived host pinned at <see cref="Clock"/>, booted before any fact seeds (PAT-008).</summary>
    private WebApplicationFactory<Program> NewHost()
    {
        var host = _fx.Factory.WithFixedToday(Clock);
        _ = host.CreateClient();
        return host;
    }

    // ─── (2) F: today each answers 200 (or, for the close, 204) ──────────────────

    /// <summary>
    /// <b>F (CI).</b> The actor sends the user edit for themselves, organisation unchanged, agreement
    /// code AC to HK dated 10 November 2026: 403, error "Access denied", the P-26 reason; nothing written.
    /// <para><b>Red conditions (on <c>3513d2b1</c> without the P-26 site at <c>AdminEndpoints.cs:2141</c>,
    /// and on master today).</b> No site asks whether the actor is the subject once the departed
    /// refusal has passed, so the edit writes the new agreement-code row and answers 200:
    /// <c>Assert.Equal(Forbidden, status)</c> fails (and the unchanged-trail assertions would).</para>
    /// </summary>
    [Fact]
    public async Task UsersPut_OwnAgreementCodeChange_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var rsp = await PutUserAsync(client, Cs, self,
            new { primaryOrgId = "STY01", agreementCode = "HK", effectiveFrom = "2026-11-10" });

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
    }

    /// <summary>
    /// <b>F (CI).</b> The same change through <c>PUT /api/admin/users/{self}/agreement-code</c>: 403 with
    /// the P-26 reason; nothing written.
    /// <para><b>Red conditions (without the P-26 site at <c>AdminEndpoints.cs:3065</c>).</b> The route
    /// writes the new row and answers 200: <c>Assert.Equal(Forbidden, status)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task AgreementCodeRoute_OwnCodeChange_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{self}/agreement-code")
        {
            Content = JsonContent.Create(new { agreementCode = "HK", effectiveFrom = "2026-11-10" }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{versionBefore}\"");
        var rsp = await client.SendAsync(req);

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
    }

    /// <summary>
    /// <b>F (CI).</b> Three cases (the fraction 1.0 to 0.8; the position null to "Specialist"; the
    /// category "Standard" to "Chefkonsulent", both taken from the existing profile fixtures): the actor's
    /// profile PUT for themselves dated today answers 403 with the P-26 reason and writes nothing.
    /// <para><b>Red conditions (without the P-26 site at <c>EmployeeProfileEndpoints.cs:577</c>).</b> The
    /// writer reports a change and the route answers 200: <c>Assert.Equal(Forbidden, status)</c> fails
    /// for each case.</para>
    /// </summary>
    [Theory]
    [InlineData("fraction")]
    [InlineData("position")]
    [InlineData("category")]
    public async Task ProfilePut_OwnPayInputChange_403_NothingWritten(string changed)
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var body = new
        {
            effectiveFrom = "2026-11-10",
            partTimeFraction = changed == "fraction" ? 0.8m : 1.0m,
            position = changed == "position" ? "Specialist" : (string?)null,
            employmentCategory = changed == "category" ? "Chefkonsulent" : (string?)null,
        };
        var rsp = await PutProfileAsync(client, self, body, versionBefore);

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
    }

    /// <summary>
    /// <b>F (CI)</b> (P-26's fourth site, ruling 5.2). The actor closes their own profile,
    /// <c>DELETE /api/admin/employee-profiles/{self}</c> with a valid If-Match: 403 with the same reason;
    /// the profile rows are unchanged, no <c>EmployeeProfileSoftDeleted</c> event, no new audit row.
    /// <para><b>Red conditions (without the unconditional self refusal at <c>EmployeeProfileEndpoints.cs:1261</c>).</b>
    /// The close runs and answers 204: <c>Assert.Equal(Forbidden, status)</c> fails (and the profile rows
    /// and the soft-deleted-event count would differ).</para>
    /// </summary>
    [Fact]
    public async Task ProfileClose_OwnProfile_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{self}");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{versionBefore}\"");
        var rsp = await client.SendAsync(req);

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
        Assert.Equal(0L, await ScalarLongAsync(Cs,
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = 'EmployeeProfileSoftDeleted'",
            ("s", $"employee-profile-{self}")));
    }

    // ─── (5) F: the top role is refused too (the actor is a GlobalAdmin whose employee id is the subject's) ───

    /// <summary>
    /// <b>F (CI)</b> (AC-C13h (5)). <see cref="UsersPut_OwnAgreementCodeChange_403_NothingWritten"/>'s request sent by
    /// a GlobalAdmin (GLOBAL scope) whose employee id IS the seeded person's: 403, the P-26 reason, nothing written.
    /// <para><b>Red conditions (on <c>ac47d9f1</c>).</b> No site asks whether the actor is the subject, so the edit
    /// writes the new code and answers 200: <c>Assert.Equal(Forbidden, status)</c> fails. A refusal keyed on the actor's
    /// role (LocalHR only) would also fail it: that is why the top role is the discriminating one.</para>
    /// </summary>
    [Fact]
    public async Task UsersPut_OwnAgreementCodeChange_GlobalAdmin_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = GlobalAdminClient(host, self);
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var rsp = await PutUserAsync(client, Cs, self,
            new { primaryOrgId = "STY01", agreementCode = "HK", effectiveFrom = "2026-11-10" });

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13h (5)). <see cref="AgreementCodeRoute_OwnCodeChange_403_NothingWritten"/>'s request sent by
    /// the GlobalAdmin whose employee id is the subject's: 403, the P-26 reason, nothing written.
    /// <para><b>Red conditions (on <c>ac47d9f1</c>).</b> The route writes the new row and answers 200:
    /// <c>Assert.Equal(Forbidden, status)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task AgreementCodeRoute_OwnCodeChange_GlobalAdmin_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = GlobalAdminClient(host, self);
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{self}/agreement-code")
        {
            Content = JsonContent.Create(new { agreementCode = "HK", effectiveFrom = "2026-11-10" }),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{versionBefore}\"");
        var rsp = await client.SendAsync(req);

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13h (5)). The fraction case of <see cref="ProfilePut_OwnPayInputChange_403_NothingWritten"/>
    /// (1.0 to 0.8, dated today) sent by the GlobalAdmin whose employee id is the subject's: 403, the P-26 reason,
    /// nothing written.
    /// <para><b>Red conditions (on <c>ac47d9f1</c>).</b> The writer reports a change and the route answers 200:
    /// <c>Assert.Equal(Forbidden, status)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task ProfilePut_OwnPayInputChange_GlobalAdmin_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = GlobalAdminClient(host, self);
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var rsp = await PutProfileAsync(client, self, new
        {
            effectiveFrom = "2026-11-10",
            partTimeFraction = 0.8m,
            position = (string?)null,
            employmentCategory = (string?)null,
        }, versionBefore);

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13h (5)). <see cref="ProfileClose_OwnProfile_403_NothingWritten"/>'s DELETE (valid If-Match)
    /// sent by the GlobalAdmin whose employee id is the subject's: 403, the P-26 reason, nothing written.
    /// <para><b>Red conditions (on <c>ac47d9f1</c>).</b> The close runs and answers 204:
    /// <c>Assert.Equal(Forbidden, status)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task ProfileClose_OwnProfile_GlobalAdmin_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = GlobalAdminClient(host, self);
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{self}");
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{versionBefore}\"");
        var rsp = await client.SendAsync(req);

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
    }

    // ─── (6) F: the self close is refused before the If-Match is read (accepted deviation 6) ───

    /// <summary>
    /// <b>F (CI)</b> (AC-C13h (6)). The LocalHR self sends the DELETE of their own profile with NO If-Match header: 403,
    /// the P-26 reason, nothing written, and not 428 (precondition required).
    /// <para><b>Red conditions (on <c>ac47d9f1</c>).</b> The close parses the If-Match before anything else and answers
    /// 428: <c>Assert.True(status == Forbidden)</c> fails with 428. It also fails if a later edit moves the self check
    /// into the transaction, behind the If-Match parse.</para>
    /// </summary>
    [Fact]
    public async Task ProfileClose_OwnProfile_NoIfMatch_403_Not428()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");
        var versionBefore = await UsersVersionAsync(self);
        var trailBefore = await WriteTrailAsync(Cs, self);

        var rsp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/employee-profiles/{self}"));

        await AssertSelfRefusedAsync(rsp);
        Assert.Equal(versionBefore, await UsersVersionAsync(self));
        Assert.Equal(trailBefore, await WriteTrailAsync(Cs, self));
    }

    // ─── (3) R: today each answers 200, and must keep doing so ───────────────────

    /// <summary>
    /// <b>R.</b> The drawer's full payload for themselves (<c>useEditPerson.ts:209-220</c>): the user edit
    /// with the unchanged organisation, agreement code and today's date and a new e-mail answers 200; then
    /// the profile PUT with the unchanged fraction and position dated today answers 200 (the writer
    /// reports no change). Discriminating assertion: both 200s and the new e-mail stored.
    /// <para><b>Red conditions.</b> A P-26 that refused on a field's presence, or on "the actor is the
    /// subject" alone, answers 403 on either PUT: <c>Assert.Equal(OK, status)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task DrawerSelfSave_OwnEmail_UnchangedPayInputs_200_OnBothPuts()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");
        var displayName = await ScalarStringAsync(Cs, "SELECT display_name FROM users WHERE user_id = @u", ("u", self));

        var userPut = await PutUserAsync(client, Cs, self, new
        {
            effectiveFrom = "2026-11-10",
            displayName,
            email = "s148.self.save@example.org",
            primaryOrgId = "STY01",
            agreementCode = "AC",
        });
        Assert.Equal(HttpStatusCode.OK, userPut.StatusCode);

        var versionNow = await UsersVersionAsync(self);
        var profilePut = await PutProfileAsync(client, self, new
        {
            effectiveFrom = "2026-11-10",
            partTimeFraction = 1.0m,
            position = (string?)null,
            employmentCategory = (string?)null,
        }, versionNow);
        Assert.Equal(HttpStatusCode.OK, profilePut.StatusCode);

        Assert.Equal("s148.self.save@example.org",
            await ScalarStringAsync(Cs, "SELECT email FROM users WHERE user_id = @u", ("u", self)));
    }

    /// <summary>
    /// <b>R.</b> The user edit for themselves with only a new display name answers 200 and stores it.
    /// <para><b>Red conditions.</b> A P-26 that refused every self edit would answer 403:
    /// <c>Assert.Equal(OK, status)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task OwnDisplayNameChange_200()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");

        var rsp = await PutUserAsync(client, Cs, self, new { displayName = "S148 Renamed Self" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("S148 Renamed Self",
            await ScalarStringAsync(Cs, "SELECT display_name FROM users WHERE user_id = @u", ("u", self)));
    }

    // ─── (4) R: the departed refusal still answers first ─────────────────────────

    /// <summary>
    /// <b>R</b> (BC-1, ruled 1.6). An HR user past their last day and still active (end date 4 March 2026,
    /// the clock at 10 November 2026) moves themselves to STY02, which their scope covers: 403 whose
    /// <c>reason</c> is the departed refusal's (<see cref="DepartedSubject.DenyReason"/>). Discriminating
    /// assertion: the reason string.
    /// <para><b>Red conditions.</b> A refusal order that put TASK-14820's self-transfer reason or P-26's
    /// ahead of the departed one answers 403 with the other string: the reason assertion fails. The
    /// fact passes on today's code (the inline departed guard runs first, <c>AdminEndpoints.cs:1696-1698</c>),
    /// which is why it is R, not F.</para>
    /// </summary>
    [Fact]
    public async Task DepartedSelfTransfer_GetsTheDepartedRefusal_NotTheSelfOne()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        await using (var conn = new NpgsqlConnection(Cs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "UPDATE users SET employment_end_date = DATE '2026-03-04' WHERE user_id = @u", conn);
            cmd.Parameters.AddWithValue("u", self);
            await cmd.ExecuteNonQueryAsync();
        }
        var client = LocalHrClient(host, self, "STY01", "STY02");

        var rsp = await PutUserAsync(client, Cs, self, new { primaryOrgId = "STY02" });

        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
        var json = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DepartedSubject.DenyReason, json.GetProperty("reason").GetString());
        Assert.Equal("STY01", await UsersPrimaryOrgAsync(Cs, self));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────────

    private static async Task AssertSelfRefusedAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(SelfReason, doc.RootElement.GetProperty("reason").GetString());
    }

    private Task<long> UsersVersionAsync(string userId) =>
        ScalarLongAsync(Cs, "SELECT version FROM users WHERE user_id = @u", ("u", userId));

    /// <summary>The profile PUT with the If-Match the test names (the users version, the aggregate token).</summary>
    private static async Task<HttpResponseMessage> PutProfileAsync(
        HttpClient client, string employeeId, object body, long ifMatchVersion)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employee-profiles/{employeeId}")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatchVersion}\"");
        return await client.SendAsync(req);
    }

    /// <summary>
    /// Everything a refused or racing write must leave alone, as one comparable string: the person's agreement-code
    /// and profile rows (versions and timestamps included), the audit counts of the four audited tables, every event
    /// on a stream that ends with the person's id, the audit projection rows about the person, the worklist rows,
    /// the settlement rows and their audit, and the users columns a change to the employment dates, the e-mail, the
    /// name or the active flag would touch. It deliberately leaves out <c>users.version</c> and
    /// <c>users.primary_org_id</c>: callers assert those themselves (the organisation-drift facts move them on purpose).
    /// </summary>
    internal static Task<string?> WriteTrailAsync(string cs, string userId) => ScalarStringAsync(cs,
        """
        SELECT concat_ws('|',
          (SELECT COALESCE(string_agg(a::text, ';' ORDER BY a.effective_from, a.assignment_id::text), '')
             FROM user_agreement_codes a WHERE a.user_id = @u),
          (SELECT COALESCE(string_agg(p::text, ';' ORDER BY p.effective_from, p.profile_id::text), '')
             FROM employee_profiles p WHERE p.employee_id = @u),
          (SELECT COUNT(*) FROM users_audit WHERE user_id = @u),
          (SELECT COUNT(*) FROM user_agreement_codes_audit WHERE user_id = @u),
          (SELECT COUNT(*) FROM user_organisations_audit WHERE user_id = @u),
          (SELECT COUNT(*) FROM employee_profile_audit WHERE employee_id = @u),
          (SELECT COUNT(*) FROM outbox_events WHERE right(stream_id, length(@u)) = @u),
          (SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @u),
          (SELECT COUNT(*) FROM hr_backdate_worklist WHERE employee_id = @u),
          (SELECT COALESCE(string_agg(s::text, ';' ORDER BY s.entitlement_type, s.entitlement_year, s.sequence), '')
             FROM vacation_settlements s WHERE s.employee_id = @u),
          (SELECT COUNT(*) FROM vacation_settlement_audit WHERE employee_id = @u),
          (SELECT concat_ws(',', display_name, email, employment_start_date::text, employment_end_date::text,
                            is_active::text, agreement_code)
             FROM users WHERE user_id = @u))
        """, ("u", userId));
}
