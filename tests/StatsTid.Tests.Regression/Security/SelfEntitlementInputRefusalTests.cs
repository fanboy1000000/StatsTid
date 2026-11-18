using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S148 / TASK-14845-T (AC-C13q (1)-(6); the owner's ruling of 2026-11-18; RES-003; SEC-065) - nobody changes their own
/// entitlement eligibility or their own birth date.
///
/// <para>
/// <b>In plain language.</b> An HR user who is also a person in the system may not set their own child's-sick-day eligibility
/// and may not change their own birth date (which decides when their senior days start): a second administrator must do it.
/// A GlobalAdmin may not either. The request answers 403 with one fixed sentence and writes nothing, and it does so before
/// anything else is checked, so even a request that forgot its version header gets the refusal and not "precondition required".
/// Editing anyone else's eligibility or birth date works as before.
/// </para>
///
/// <para>
/// <b>Fixture.</b> The Backend host with the clock fixed at Tuesday 10 November 2026 (<see cref="Clock"/>), the shape of
/// <see cref="SelfChangeRefusalTests"/> (one container and one booted host for the class, each fact seeding its own person in
/// STY01). The actor's employee id IS the subject's: a LocalHR scoped to STY01, or a GlobalAdmin with GLOBAL scope. The 403 body
/// is contract: <c>error</c> "Access denied" and <c>reason</c> the literal <see cref="SelfReason"/>, never the production constant.
/// "Nothing written" is <see cref="TrailAsync"/> before and after plus <c>users.version</c>. The preconditions are valid on purpose
/// (the birth-date facts send the person's real version, the eligibility facts <c>If-None-Match: *</c> on a person with no row), so
/// the red on the code without the self refusal is the 200 and not a 412 or a 409. Docker-gated: <b>CI-pending</b>. F (CI) facts: no
/// mutation (an F fact needs none).
/// </para>
///
/// <para>
/// <b>What keeps the refusal narrow</b> (no new fact): the existing facts that send these PUTs as an HR user or a GlobalAdmin on
/// ANOTHER person answer 200 today and stay green at CI-W4; they turn red if the refusal fired for every actor:
/// <c>SkemaEntitlementEligibilityGuardTests.ChildSick_AfterHrGrantsEligibility_PostSave_Succeeds_AndPersists</c>,
/// <c>EligibilityWrite_BlindRecreateOnLiveRow_Returns409_IfMatchEnforcesVersion</c> and
/// <c>GetEligibility_BeforeSet_RowExistsFalse_NoEtag_AfterSet_StateAndEtag</c>;
/// <c>S115EmployeeFieldSpecRuntimeTests.BirthDate_Put200_SchemaMatchesRuntime</c> and
/// <c>EntitlementEligibility_Put200_CreateBranch_SchemaMatchesRuntime</c>;
/// <c>EmployeeProfileCopenhagenBoundaryTests.Eligibility_AfterDanishMidnight_StampsEffectiveFromOnTheDanishDay</c>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SelfEntitlementInputRefusalTests : IClassFixture<DepartedSelfHostFixture>
{
    /// <summary>The refusal's reason as a literal (the brief's contract item 11), never the production constant.</summary>
    private const string SelfReason =
        "Own entitlement eligibility and birth date cannot be modified; a second administrator must perform this change";

    private readonly DepartedSelfHostFixture _fx;

    public SelfEntitlementInputRefusalTests(DepartedSelfHostFixture fx) => _fx = fx;

    private string Cs => _fx.ConnectionString;

    /// <summary>A derived host pinned at <see cref="Clock"/>, booted before any fact seeds (PAT-008).</summary>
    private WebApplicationFactory<Program> NewHost()
    {
        var host = _fx.Factory.WithFixedToday(Clock);
        _ = host.CreateClient();
        return host;
    }

    // ─── eligibility PUT ────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI)</b> (AC-C13q (1)). The LocalHR self sets their own CHILD_SICK eligibility (<c>{ "eligible": true }</c>,
    /// <c>If-None-Match: *</c>, the person having no row): 403, "Access denied", the self reason; no eligibility row, no eligibility
    /// audit row, no event on <c>employee-entitlement-eligibility-{self}-CHILD_SICK</c>, the person's audit-projection count and
    /// <c>users.version</c> unchanged.
    /// <para><b>Red conditions (on <c>446ec114</c>/<c>ba0a054e</c>, without the self refusal at
    /// <c>EntitlementEligibilityEndpoints.cs</c>' eligibility PUT).</b> The route asks only the scope check, which admits an HR user on
    /// their own record, and the write lands (the row created at version 1, 200): <c>Assert.True(status == Forbidden)</c> fails
    /// with 200.</para>
    /// </summary>
    [Fact]
    public async Task EligibilityPut_OwnEligibility_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");

        await AssertRefusedWritingNothingAsync(self,
            () => EligibilityRequest(self, ifNoneMatchStar: true), client);
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13q (2)). The same request by a GlobalAdmin whose employee id is the person's (GLOBAL scope, the top role,
    /// so a role-gated bypass would show): 403 and the same refusal; nothing written.
    /// <para><b>Red conditions.</b> A GlobalAdmin passes the scope check on anyone, themselves included, and the write lands (200):
    /// <c>Assert.True(status == Forbidden)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task EligibilityPut_OwnEligibility_GlobalAdmin_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = GlobalAdminClient(host, self);

        await AssertRefusedWritingNothingAsync(self,
            () => EligibilityRequest(self, ifNoneMatchStar: true), client);
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13q (5)). The LocalHR self sends the eligibility PUT with neither <c>If-None-Match</c> nor <c>If-Match</c>:
    /// 403, not 428; nothing written (as part (1)). It pins the refusal's place: before the precondition parse.
    /// <para><b>Red conditions (on <c>446ec114</c>/<c>ba0a054e</c>).</b> The precondition parse answers 428
    /// (<c>EntitlementEligibilityEndpoints.cs:132-134</c> at that base): <c>Assert.True(status == Forbidden)</c> fails with
    /// 428. It also fails if a later edit moves the self check behind the precondition parse.</para>
    /// </summary>
    [Fact]
    public async Task EligibilityPut_OwnEligibility_NoPrecondition_403_Not428()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");

        await AssertRefusedWritingNothingAsync(self,
            () => EligibilityRequest(self, ifNoneMatchStar: false), client);
    }

    // ─── birth-date PUT ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>F (CI)</b> (AC-C13q (3)). The LocalHR self changes their own birth date (<c>{ "birthDate": "1980-03-12" }</c>,
    /// <c>If-Match</c> the person's real <c>users.version</c>): 403, "Access denied", the self reason; <c>users.version</c>, the birth
    /// date and the person's <c>users_audit</c> count unchanged.
    /// <para><b>Red conditions.</b> The route asks only the scope check and the write lands (the version bumped, a <c>users_audit</c>
    /// row written, 200): <c>Assert.True(status == Forbidden)</c> fails with 200.</para>
    /// </summary>
    [Fact]
    public async Task BirthDatePut_OwnBirthDate_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");
        var version = await VersionAsync(self);

        await AssertRefusedWritingNothingAsync(self, () => BirthDateRequest(self, version), client);
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13q (4)). The same request by a GlobalAdmin whose employee id is the person's: 403 and the same refusal;
    /// nothing written.
    /// <para><b>Red conditions.</b> A GlobalAdmin passes the scope check on themselves and the write lands (200):
    /// <c>Assert.True(status == Forbidden)</c> fails.</para>
    /// </summary>
    [Fact]
    public async Task BirthDatePut_OwnBirthDate_GlobalAdmin_403_NothingWritten()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = GlobalAdminClient(host, self);
        var version = await VersionAsync(self);

        await AssertRefusedWritingNothingAsync(self, () => BirthDateRequest(self, version), client);
    }

    /// <summary>
    /// <b>F (CI)</b> (AC-C13q (6)). The LocalHR self sends the birth-date PUT with no <c>If-Match</c>: 403, not 428; nothing written
    /// (as part (3)). It pins the refusal's place: before the If-Match parse.
    /// <para><b>Red conditions (on <c>446ec114</c>/<c>ba0a054e</c>).</b> The If-Match parse answers 428
    /// (<c>EntitlementEligibilityEndpoints.cs:391-393</c> at that base): <c>Assert.True(status == Forbidden)</c> fails with 428. It
    /// also fails if a later edit moves the self check behind the parse.</para>
    /// </summary>
    [Fact]
    public async Task BirthDatePut_OwnBirthDate_NoIfMatch_403_Not428()
    {
        using var host = NewHost();
        var self = await SeedPersonAsync(Cs, "STY01");
        var client = LocalHrClient(host, self, "STY01");

        await AssertRefusedWritingNothingAsync(self, () => BirthDateRequest(self, ifMatchVersion: null), client);
    }

    // ─── helpers ────────────────────────────────────────────────────────────────────

    private static HttpRequestMessage EligibilityRequest(string self, bool ifNoneMatchStar)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employees/{self}/entitlement-eligibility/CHILD_SICK")
        {
            Content = JsonContent.Create(new { eligible = true }),
        };
        if (ifNoneMatchStar)
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return req;
    }

    private static HttpRequestMessage BirthDateRequest(string self, long? ifMatchVersion)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employees/{self}/birth-date")
        {
            Content = JsonContent.Create(new { birthDate = "1980-03-12" }),
        };
        if (ifMatchVersion is long v)
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{v}\"");
        return req;
    }

    private async Task AssertRefusedWritingNothingAsync(string self, Func<HttpRequestMessage> build, HttpClient client)
    {
        var versionBefore = await VersionAsync(self);
        var trailBefore = await TrailAsync(self);

        var rsp = await client.SendAsync(build());

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(SelfReason, doc.RootElement.GetProperty("reason").GetString());

        Assert.Equal(versionBefore, await VersionAsync(self));
        Assert.Equal(trailBefore, await TrailAsync(self));
    }

    private Task<long> VersionAsync(string userId) =>
        ScalarLongAsync(Cs, "SELECT version FROM users WHERE user_id = @u", ("u", userId));

    /// <summary>
    /// What these two requests write, as one comparable string: the eligibility rows and their audit rows, the events on the
    /// eligibility stream (whose id ends in the entitlement type, not the person's id, so
    /// <see cref="SelfChangeRefusalTests.WriteTrailAsync"/>'s "ends with the person's id" clause misses it), every event naming the
    /// person, the audit-projection rows about the person, the <c>users_audit</c> rows and the birth date.
    /// </summary>
    private Task<string?> TrailAsync(string userId) => ScalarStringAsync(Cs,
        """
        SELECT concat_ws('|',
          (SELECT COUNT(*) FROM employee_entitlement_eligibility WHERE employee_id = @u),
          (SELECT COUNT(*) FROM employee_entitlement_eligibility_audit WHERE employee_id = @u),
          (SELECT COUNT(*) FROM outbox_events WHERE stream_id = 'employee-entitlement-eligibility-' || @u || '-CHILD_SICK'),
          (SELECT COUNT(*) FROM outbox_events WHERE stream_id LIKE '%' || @u || '%' OR event_payload::text LIKE '%' || @u || '%'),
          (SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @u),
          (SELECT COUNT(*) FROM users_audit WHERE user_id = @u),
          (SELECT COALESCE(birth_date::text, 'none') FROM users WHERE user_id = @u))
        """, ("u", userId));
}
