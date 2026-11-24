using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Payroll;
using StatsTid.Tests.Regression.Security;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14836-T (AC-C11a, -11b, -11c's route half, -11d, -11f; the security consult's added negatives;
/// ADR-040, ADR-043 D4, RES-003) — HR records two dated facts about a person (the overtime regime and flex
/// membership), supersedes them from a later date, and reads them as of any day; each write is audited; one's own
/// facts, a departed person's and another organisation's are refused; a value outside the person's agreement is
/// refused; and an edit is no payroll boundary.
///
/// <para>
/// <b>In plain language.</b> Lise, an HR user at STY01, records that Jonas (HK) earns overtime and is on flex from 2
/// March, then that he is exempt and off flex from 6 April. Reading as of 3 April gives the first answer, as of 6
/// April the second, and as of 1 March "not recorded". Each save leaves one audit row, one event and one audit-view
/// row for STY01. Lise cannot record her own facts (nor can a GlobalAdmin record theirs); HR of another
/// organisation, or with only a leader's scope over the person, is refused; a departed person is refused; a regime
/// the person's agreement does not allow, an unknown regime, a date in the future or before the current record, a
/// missing or stale version, and a person moved to another organisation while HR was saving are each refused with
/// nothing written.
/// </para>
///
/// <para>
/// <b>Fixture.</b> One container and one booted Backend host per class (<see cref="DepartedSelfHostFixture"/>: no
/// leaver job, so a seeded departed person stays as seeded), each fact seeding its own person in STY01
/// (<c>SeedPersonAsync</c>) and acting as Lise (<c>HR_S148_C11</c>, LocalHR of STY01, not a seeded person) unless it
/// names another actor; the clock is Tuesday 10 November 2026. "Nothing written" is <see cref="TrailAsync"/> (the
/// person's facts rows, facts-audit rows, events on <c>employee-hr-facts-{id}</c> and audit-view rows) and
/// <c>users.version</c>, before and after the one request. No fact here deletes a user, so no foreign-key clean-up
/// is needed (the seed fact does its own). Docker-gated: <b>CI-pending</b>. Every route fact is F: on
/// <c>1cc0cb1d</c> the routes do not exist, so each answers 404 and its first status assertion fails.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeHrFactsTests : IClassFixture<DepartedSelfHostFixture>, IAsyncLifetime
{
    private const string Lise = "HR_S148_C11";

    private const string SelfReason =
        "Own HR facts cannot be modified; a second administrator must perform this change";

    private const string DriftError =
        "The user's organisation changed while this request was being processed; refresh and retry.";

    private const string ScopeReason = "Actor scope does not cover target organization";
    private const string NotFoundReason = "Target employee not found";
    private const string DepartedReason = "A departed employee cannot act on their own record";

    /// <summary>A Wednesday in March 2026: before the clock by far, so a person ending then is departed (and, for any
    /// other actor, past the leaver grace).</summary>
    private static readonly DateOnly Gone = new(2026, 3, 4);

    private static long _outboxSeq = 148_360_000;

    private readonly DepartedSelfHostFixture _fx;
    private WebApplicationFactory<Program> _host = null!;

    public EmployeeHrFactsTests(DepartedSelfHostFixture fx) => _fx = fx;

    private string Cs => _fx.ConnectionString;

    public Task InitializeAsync()
    {
        _host = _fx.Factory.WithFixedToday(Clock);
        _ = _host.CreateClient(); // boot before any fact seeds (PAT-008)
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _host?.Dispose();
        return Task.CompletedTask;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  AC-C11a — record, supersede, read as of a day; audited
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI).</b> Jonas (HK): record from Monday 2 March 2026, supersede from Monday 6 April 2026, read as of three
    /// days. Each write leaves its row change, one audit row, one event on <c>employee-hr-facts-{Jonas}</c> and one
    /// projection row targeting STY01.
    /// <para><b>Product code between the readings:</b> the PUTs (checks, the transaction) and the GETs (the resolver).</para>
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404, so the record's
    /// <c>Assert.Equal(HttpStatusCode.OK, …)</c> fails first.</para>
    /// </summary>
    [Fact]
    public async Task RecordSupersedeReadAsOfDay_Audited()
    {
        var jonas = await SeedPersonAsync(Cs, "STY01", "HK");
        var lise = LiseClient();

        // 1. Record.
        var created = await PutAsync(lise, jonas,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var rows = await ReadRowsAsync(jonas);
        var first = Assert.Single(rows);
        Assert.Equal(("OVERTIME", (bool?)true, new DateOnly(2026, 3, 2), (DateOnly?)null, 1L),
            (first.Regime, first.Flex, first.From, first.To, first.Version));
        var audit = await ReadAuditAsync(jonas);
        var createdAudit = Assert.Single(audit);
        Assert.Equal(("CREATED", (long?)null, 1L, Lise), (createdAudit.Action, createdAudit.VersionBefore, createdAudit.VersionAfter, createdAudit.ActorId));
        var events = await ReadEventsAsync(jonas);
        var createdEvent = Assert.Single(events);
        Assert.Equal("CREATED", createdEvent.Payload.GetProperty("action").GetString());
        Assert.Equal("OVERTIME", createdEvent.Payload.GetProperty("overtimeRegime").GetString());
        Assert.True(createdEvent.Payload.GetProperty("onFlexScheme").GetBoolean());
        Assert.Equal("2026-03-02", createdEvent.Payload.GetProperty("effectiveFrom").GetString());
        Assert.Equal(1L, createdEvent.Payload.GetProperty("versionAfter").GetInt64());
        await AssertProjectionAsync(createdEvent.EventId, jonas);

        // 2. Supersede.
        var superseded = await PutAsync(lise, jonas,
            new { effectiveFrom = "2026-04-06", overtimeRegime = "EXEMPT", onFlexScheme = false }, ifMatch: "1");
        Assert.Equal(HttpStatusCode.OK, superseded.StatusCode);
        rows = await ReadRowsAsync(jonas);
        Assert.Equal(2, rows.Count);
        Assert.Equal((new DateOnly(2026, 3, 2), (DateOnly?)new DateOnly(2026, 4, 6), 1L),
            (rows[0].From, rows[0].To, rows[0].Version));
        Assert.Equal(("EXEMPT", (bool?)false, new DateOnly(2026, 4, 6), (DateOnly?)null, 2L),
            (rows[1].Regime, rows[1].Flex, rows[1].From, rows[1].To, rows[1].Version));
        audit = await ReadAuditAsync(jonas);
        Assert.Equal(2, audit.Count);
        Assert.Equal(("SUPERSEDED", (long?)1L, 2L, Lise),
            (audit[1].Action, audit[1].VersionBefore, audit[1].VersionAfter, audit[1].ActorId));
        events = await ReadEventsAsync(jonas);
        Assert.Equal(2, events.Count);
        var supersededEvent = events[1];
        Assert.Equal("SUPERSEDED", supersededEvent.Payload.GetProperty("action").GetString());
        Assert.Equal(rows[0].FactsId, supersededEvent.Payload.GetProperty("precedingFactsId").GetGuid());
        Assert.Equal(1L, supersededEvent.Payload.GetProperty("versionBefore").GetInt64());
        Assert.Equal(2L, supersededEvent.Payload.GetProperty("versionAfter").GetInt64());
        await AssertProjectionAsync(supersededEvent.EventId, jonas);

        // 3. Read as of a day.
        var apr3 = await GetFactsAsync(lise, jonas, "2026-04-03");
        Assert.Equal("OVERTIME", apr3.GetProperty("overtimeRegime").GetString());
        Assert.True(apr3.GetProperty("onFlexScheme").GetBoolean());
        Assert.Equal("2026-03-02", apr3.GetProperty("effectiveFrom").GetString());
        AssertHkAgreement(apr3);

        var apr6 = await GetFactsAsync(lise, jonas, "2026-04-06");
        Assert.Equal("EXEMPT", apr6.GetProperty("overtimeRegime").GetString());
        Assert.False(apr6.GetProperty("onFlexScheme").GetBoolean());
        Assert.Equal("2026-04-06", apr6.GetProperty("effectiveFrom").GetString());
        AssertHkAgreement(apr6);
        Assert.Equal(2L, apr6.GetProperty("liveVersion").GetInt64());
        Assert.Equal("2026-04-06", apr6.GetProperty("liveEffectiveFrom").GetString());

        var mar1 = await GetFactsAsync(lise, jonas, "2026-03-01");
        Assert.Equal(JsonValueKind.Null, mar1.GetProperty("overtimeRegime").ValueKind);
        Assert.Equal(JsonValueKind.Null, mar1.GetProperty("onFlexScheme").ValueKind);
        AssertHkAgreement(mar1);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  AC-C11b — self-edit, departed subject, another organisation; plus the consult's access negatives
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI), a Theory over six arms.</b> The person's employee id IS the actor's. Each answers 403 with exactly
    /// <c>{ error: "Access denied", reason: &lt;the self reason&gt; }</c> and writes nothing: (1) a LocalHR self with
    /// <c>If-None-Match: *</c>; (2) a GlobalAdmin self; (3) a LocalHR self with no precondition header (403, not 428);
    /// and, from the consult (row 3), a LocalHR self with (4) a date in the future, (5) an unknown regime and (6) a stale
    /// <c>If-Match: "7"</c>: the self refusal is before the precondition and the validation (403, not 428, 400 or 412).
    /// <para><b>Product code between the readings:</b> the PUT's scope check and self refusal.</para>
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> each answers 404: <c>Assert.Equal(Forbidden, …)</c> fails. On a
    /// code where the self check moves behind the precondition or the validation, arms 3-6 answer 428, 400, 400 and 412.</para>
    /// </summary>
    [Theory]
    [InlineData("LocalHr_IfNoneMatch")]
    [InlineData("GlobalAdmin_IfNoneMatch")]
    [InlineData("LocalHr_NoPrecondition")]
    [InlineData("LocalHr_FutureDate")]
    [InlineData("LocalHr_UnknownRegime")]
    [InlineData("LocalHr_StaleIfMatch")]
    public async Task SelfEdit_403(string arm)
    {
        var self = await SeedPersonAsync(Cs, "STY01", "HK");
        var client = arm.StartsWith("GlobalAdmin", StringComparison.Ordinal) ? GlobalAdminClient(_host, self) : LocalHrClient(_host, self, "STY01");
        var body = arm switch
        {
            "LocalHr_FutureDate" => (object)new { effectiveFrom = "2026-12-01", overtimeRegime = "OVERTIME", onFlexScheme = true },
            "LocalHr_UnknownRegime" => new { overtimeRegime = "BOGUS", onFlexScheme = true },
            _ => new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true },
        };
        var before = await TrailAsync(self);

        using var rsp = arm switch
        {
            "LocalHr_NoPrecondition" => await PutAsync(client, self, body),
            "LocalHr_StaleIfMatch" => await PutAsync(client, self, body, ifMatch: "7"),
            _ => await PutAsync(client, self, body, ifNoneMatchStar: true),
        };

        await AssertDeniedAsync(rsp, SelfReason);
        Assert.Equal(before, await TrailAsync(self));
    }

    /// <summary>
    /// <b>F (CI)</b> (consult row 4). A self whose only scope covering their organisation is a leader's (a LocalHR of
    /// STY02 who is also LocalLeader of STY01, and is the STY01 person): 403 with the validator's scope reason, not the
    /// self reason; nothing written. Pins the order scope check, then self refusal.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> 404. If the self check ran before the scope check the reason would
    /// be the self reason and <c>Assert.Equal(ScopeReason, reason)</c> would fail.</para>
    /// </summary>
    [Fact]
    public async Task SelfEdit_ScopeBelowLocalHr_ScopeReasonNotSelfReason()
    {
        var self = await SeedPersonAsync(Cs, "STY01", "HK");
        var client = ClientWith(self, StatsTidRoles.LocalHR,
            new RoleScope(StatsTidRoles.LocalHR, "STY02", "ORG_ONLY"),
            new RoleScope(StatsTidRoles.LocalLeader, "STY01", "ORG_ONLY"));
        var before = await TrailAsync(self);

        using var rsp = await PutAsync(client, self,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);

        await AssertDeniedAsync(rsp, ScopeReason);
        Assert.Equal(before, await TrailAsync(self));
    }

    /// <summary>
    /// <b>F (CI), a Theory over two arms.</b> The person is deactivated (<c>is_active = false</c>) and the actor is Lise:
    /// (1) 403 <c>Target employee not found</c>; the person is active with a last day in March (past the leaver grace
    /// by date) and the actor is Lise: (2) the same reason; the actor is the person, still active, last day before the
    /// clock: (3) 403 <c>A departed employee cannot act on their own record</c>, not the self reason (the validator's
    /// clause answers first). Each on the PUT and on the GET; the PUT writes nothing.
    /// <para><b>Product code between the readings:</b> the scope check, on both routes.</para>
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> each answers 404 (no route): <c>Assert.Equal(Forbidden, …)</c> fails.</para>
    /// </summary>
    [Theory]
    [InlineData("Deactivated")]
    [InlineData("PastGraceByDate")]
    [InlineData("DepartedSelf")]
    public async Task DepartedSubject_Refused(string arm)
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        if (arm == "Deactivated")
            await ExecAsync("UPDATE users SET is_active = FALSE, updated_at = NOW() WHERE user_id = @u", ("u", person));
        else
            await ExecAsync(
                "UPDATE users SET employment_end_date = @e, updated_at = NOW() WHERE user_id = @u",
                ("e", Gone), ("u", person));
        var client = arm == "DepartedSelf" ? LocalHrClient(_host, person, "STY01") : LiseClient();
        var reason = arm == "DepartedSelf" ? DepartedReason : NotFoundReason;
        var before = await TrailAsync(person);

        using var put = await PutAsync(client, person,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);
        await AssertDeniedAsync(put, reason);
        using var get = await client.GetAsync($"/api/admin/employees/{person}/hr-facts?asOf=2026-03-02");
        await AssertDeniedAsync(get, reason);
        Assert.Equal(before, await TrailAsync(person));
    }

    /// <summary>
    /// <b>F (CI), a Theory over three arms</b> (consult rows 7-9). A person in STY01 and: (1) a LocalHR scoped to STY02
    /// only; (2) a mixed-role actor, LocalHR of STY02 who also holds a LocalLeader scope over STY01 (FAIL-001's class: a
    /// scope below the LocalHR floor never admits); (3) a LocalHR scoped ORG_ONLY to MIN01, STY01's parent (coverage is
    /// exact). Each, on the PUT and on the GET: 403 <c>Actor scope does not cover target organization</c>; nothing
    /// written, and the GET body names no facts.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> each answers 404. A missing LocalHR floor admits arm 2 on the GET
    /// (200 with facts).</para>
    /// </summary>
    [Theory]
    [InlineData("OtherOrganisationOnly")]
    [InlineData("MixedRoleLeaderOverTarget")]
    [InlineData("ParentOrganisationOnly")]
    public async Task OtherOrganisationLocalHr_403(string arm)
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        var recorded = await PutAsync(LiseClient(), person,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode); // facts exist, so a leaking GET would show them
        var actor = "hr_s148_c11_" + arm.ToLowerInvariant();
        var client = arm switch
        {
            "OtherOrganisationOnly" => LocalHrClient(_host, actor, "STY02"),
            "MixedRoleLeaderOverTarget" => ClientWith(actor, StatsTidRoles.LocalHR,
                new RoleScope(StatsTidRoles.LocalHR, "STY02", "ORG_ONLY"),
                new RoleScope(StatsTidRoles.LocalLeader, "STY01", "ORG_ONLY")),
            _ => LocalHrClient(_host, actor, "MIN01"),
        };
        var before = await TrailAsync(person);

        using var put = await PutAsync(client, person,
            new { effectiveFrom = "2026-04-06", overtimeRegime = "EXEMPT", onFlexScheme = false }, ifMatch: "1");
        await AssertDeniedAsync(put, ScopeReason);
        using var get = await client.GetAsync($"/api/admin/employees/{person}/hr-facts?asOf=2026-03-02");
        await AssertDeniedAsync(get, ScopeReason);
        Assert.DoesNotContain("overtimeRegime", await get.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await TrailAsync(person));
    }

    /// <summary>
    /// <b>F (CI), a Theory over two arms</b> (consult row 10). An Employee token and a Leader token: 403 at the policy on the
    /// PUT and on the GET (no house body), nothing written.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route is absent: 404, not 403.</para>
    /// </summary>
    [Theory]
    [InlineData("Employee")]
    [InlineData("Leader")]
    public async Task BelowPolicyRoles_403(string arm)
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        var actor = "s148_c11_" + arm.ToLowerInvariant();
        var role = arm == "Employee" ? StatsTidRoles.Employee : StatsTidRoles.LocalLeader;
        var client = ClientWith(actor, role, new RoleScope(role, "STY01", "ORG_ONLY"));
        var before = await TrailAsync(person);

        using var put = await PutAsync(client, person,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        using var get = await client.GetAsync($"/api/admin/employees/{person}/hr-facts");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Equal(before, await TrailAsync(person));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  AC-C11c — the route half: a value outside the agreement; a no-longer-allowed value
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI), a Theory over two arms</b> (consult row 14). An HK person with <c>DISCRETIONARY</c>; an AC person with
    /// <c>OVERTIME</c>: 400, <c>fields</c> exactly <c>[{ field: "OvertimeRegime", code: "NOT_ALLOWED_FOR_AGREEMENT" }]</c>,
    /// nothing written.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404: <c>Assert.Equal(BadRequest, …)</c> fails.</para>
    /// </summary>
    [Theory]
    [InlineData("HK", "DISCRETIONARY")]
    [InlineData("AC", "OVERTIME")]
    public async Task OutsideAgreementSubset_400(string agreement, string regime)
    {
        var person = await SeedPersonAsync(Cs, "STY01", agreement);
        var before = await TrailAsync(person);

        using var rsp = await PutAsync(LiseClient(), person,
            new { effectiveFrom = "2026-11-10", overtimeRegime = regime, onFlexScheme = true }, ifNoneMatchStar: true);

        await AssertFieldRefusalAsync(rsp, "OvertimeRegime", "NOT_ALLOWED_FOR_AGREEMENT", null);
        Assert.Equal(before, await TrailAsync(person));
    }

    /// <summary>
    /// <b>F (CI).</b> An HK person recorded <c>OVERTIME</c> from Monday 2 March 2026; the agreement row is then closed at
    /// Monday 6 April 2026 and an <c>AC</c> row opened from that day (by SQL, the dated change the agreement writers
    /// leave). As of 3 April: <c>OVERTIME</c> under HK. As of 6 April: the regime is null, the agreement <c>AC</c> and
    /// the allowed set the AC one; flex membership (not tied to the agreement) still reads true.
    /// <para><b>Product code between the readings:</b> the PUT, then the resolver.</para>
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the record's PUT answers 404.</para>
    /// </summary>
    [Fact]
    public async Task NoLongerAllowed_ReadsNotRecorded()
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        var recorded = await PutAsync(LiseClient(), person,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
        await ExecAsync(
            "UPDATE user_agreement_codes SET effective_to = '2026-04-06' WHERE user_id = @u AND effective_to IS NULL", ("u", person));
        await ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @u, 'AC', '2026-04-06', NULL, 1)
            """, ("u", person));

        var before = await GetFactsAsync(LiseClient(), person, "2026-04-03");
        Assert.Equal("OVERTIME", before.GetProperty("overtimeRegime").GetString());
        Assert.Equal("HK", before.GetProperty("agreementCode").GetString());

        var after = await GetFactsAsync(LiseClient(), person, "2026-04-06");
        Assert.Equal(JsonValueKind.Null, after.GetProperty("overtimeRegime").ValueKind);
        Assert.Equal("AC", after.GetProperty("agreementCode").GetString());
        Assert.Equal(new[] { "CONTRACTUAL", "DISCRETIONARY", "NONE" },
            after.GetProperty("allowedOvertimeRegimes").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.True(after.GetProperty("onFlexScheme").GetBoolean());
    }

    /// <summary>
    /// <b>F (CI)</b> (consult rows 15 and 21's neighbours). A regime of <c>""</c>, <c>"overtime"</c> (lower case), a
    /// 10,000-character string, or a string carrying U+0000: 400, <c>fields</c> exactly
    /// <c>[{ field: "OvertimeRegime", code: "UNKNOWN_VALUE" }]</c>, never a 500; nothing written.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404. A comparison that is not ordinal-exact
    /// would let <c>"overtime"</c> through to the CHECK constraint (a 500).</para>
    /// </summary>
    [Theory]
    [InlineData("Empty")]
    [InlineData("LowerCase")]
    [InlineData("TenThousandChars")]
    [InlineData("Nul")]
    public async Task UnknownRegime_400_NeverA500(string arm)
    {
        var regime = arm switch
        {
            "Empty" => "",
            "LowerCase" => "overtime",
            "TenThousandChars" => new string('X', 10_000),
            _ => "OVERTIME\u0000",
        };
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        var before = await TrailAsync(person);

        using var rsp = await PutAsync(LiseClient(), person,
            new { effectiveFrom = "2026-11-10", overtimeRegime = regime, onFlexScheme = true }, ifNoneMatchStar: true);

        await AssertFieldRefusalAsync(rsp, "OvertimeRegime", "UNKNOWN_VALUE", null);
        Assert.Equal(before, await TrailAsync(person));
    }

    /// <summary>
    /// <b>F (CI)</b> (consult row 21, ruled by the Orchestrator: fail closed). A person whose earliest agreement row starts
    /// on Monday 1 June 2026: a non-null regime dated Monday 2 March 2026 is refused (400
    /// <c>NOT_ALLOWED_FOR_AGREEMENT</c>, nothing written); a flex-only save (regime null) on the same date succeeds and
    /// records a row with no regime and flex true.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404.</para>
    /// </summary>
    [Fact]
    public async Task BeforeTheEarliestAgreement_RegimeRefused_FlexOnlySaves()
    {
        var person = "usr_s148_hrf_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(Cs, person, "STY01", "HK", effectiveFrom: new DateOnly(2026, 6, 1));
        var before = await TrailAsync(person);

        using var refused = await PutAsync(LiseClient(), person,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);
        await AssertFieldRefusalAsync(refused, "OvertimeRegime", "NOT_ALLOWED_FOR_AGREEMENT", null);
        Assert.Equal(before, await TrailAsync(person));

        using var saved = await PutAsync(LiseClient(), person,
            new { effectiveFrom = "2026-03-02", overtimeRegime = (string?)null, onFlexScheme = true }, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var row = Assert.Single(await ReadRowsAsync(person));
        Assert.Equal(((string?)null, (bool?)true, new DateOnly(2026, 3, 2), 1L), (row.Regime, row.Flex, row.From, row.Version));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  AC-C11d — an edit is no payroll boundary
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>R (CI).</b> An HK person with a profile and an approved March 2026 (the <c>TransferPayrollBoundaryTests</c> shape).
    /// Payroll plans March (a calculate-and-export, 200), then HR records facts from Monday 2 March 2026 and supersedes
    /// them from Monday 16 March 2026 through the route (each 200), then payroll plans March again (409: the month is
    /// already exported, but the second plan is written, as in the transfer fact). The two plans' segments are
    /// identical, no segment edge falls on 16 March, and no boundary cause names the HR facts.
    /// <para><b>The set-up needs the route</b> (T-BC-3, ruling 24): the two PUTs are the product code between the
    /// readings, together with the planner.</para>
    /// <para><b>Red conditions (R).</b> The plan equality holds on any code where no payroll boundary source reads
    /// <c>employee_hr_facts</c>; it goes red if a source starts reading it (the reason the table is separate). On
    /// <c>1cc0cb1d</c> the fact fails earlier, at the set-up's first PUT (404), which is not its discriminating assertion.</para>
    /// </summary>
    [Fact]
    public async Task Edit_CreatesNoPayrollBoundary()
    {
        var person = "usr_s148_hrf_pay_" + Guid.NewGuid().ToString("N")[..8];
        var mar1 = new DateOnly(2026, 3, 1);
        var mar31 = new DateOnly(2026, 3, 31);
        await RegressionSeed.SeedEmployeeAsync(Cs, person, "STY01", "HK", "OK26", partTimeFraction: 1.0m);
        await SeedWeekdayHoursAsync(person, mar1, mar31);
        // March's first week holds February days, so the month before must be approved too (the calculation refuses otherwise).
        await SeedApprovedMonthAsync(person, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28));
        await SeedApprovedMonthAsync(person, mar1, mar31);
        var db = new DbConnectionFactory(Cs);
        await TestFixtures.SeedWageTypeMappingsAsync(db);
        await using var payroll = new PayrollHostFactory(Cs, new RuleRegistry().GetAll());

        var firstPlan = await CalculateAndExportAsync(payroll, person, mar1, mar31);
        Assert.True(firstPlan.StatusCode == HttpStatusCode.OK,
            $"expected 200, got {(int)firstPlan.StatusCode}: {await firstPlan.Content.ReadAsStringAsync()}");

        var lise = LiseClient();
        var record = await PutAsync(lise, person,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);
        Assert.True(record.StatusCode == HttpStatusCode.OK, $"record: {(int)record.StatusCode}: {await record.Content.ReadAsStringAsync()}");
        var supersede = await PutAsync(lise, person,
            new { effectiveFrom = "2026-03-16", overtimeRegime = "EXEMPT", onFlexScheme = false }, ifMatch: "1");
        Assert.True(supersede.StatusCode == HttpStatusCode.OK, $"supersede: {(int)supersede.StatusCode}: {await supersede.Content.ReadAsStringAsync()}");

        var secondPlan = await CalculateAndExportAsync(payroll, person, mar1, mar31);
        var secondJson = await secondPlan.Content.ReadAsStringAsync();
        Assert.True(
            secondPlan.StatusCode == HttpStatusCode.Conflict && secondJson.Contains("\"kind\":\"payroll-export-conflict\"", StringComparison.Ordinal),
            $"expected the 409 payroll-export-conflict, got {(int)secondPlan.StatusCode}: {secondJson}");

        var manifests = await ReadManifestsAsync(person);
        Assert.Equal(2, manifests.Count);
        Assert.Equal(manifests[0].Segments, manifests[1].Segments);
        Assert.Equal(manifests[0].CauseSummary, manifests[1].CauseSummary);
        Assert.DoesNotContain("2026-03-16", manifests[1].Segments, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-03-15", manifests[1].Segments, StringComparison.Ordinal);
        Assert.DoesNotContain("HrFacts", manifests[1].CauseSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HrFacts", manifests[1].Segments, StringComparison.OrdinalIgnoreCase);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  AC-C11f — the date, equal-values, drift and precondition answers (each writes nothing)
    // ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>F (CI)</b> (ruling 16; consult row 16). With no row: <c>effectiveFrom</c> Tuesday 1 December 2026 (after the clock)
    /// and <c>9999-12-31</c> are each refused: 400, <c>fields</c> exactly
    /// <c>[{ EffectiveFrom, EFFECTIVE_FROM_NOT_TODAY_OR_PAST, nearestValid ["2026-11-10"] }]</c>; nothing written.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404.</para>
    /// </summary>
    [Fact]
    public async Task FutureDate_400_NothingWritten()
    {
        var jonas = await SeedPersonAsync(Cs, "STY01", "HK");
        var before = await TrailAsync(jonas);

        foreach (var date in new[] { "2026-12-01", "9999-12-31" })
        {
            using var rsp = await PutAsync(LiseClient(), jonas,
                new { effectiveFrom = date, overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true);
            await AssertFieldRefusalAsync(rsp, "EffectiveFrom", "EFFECTIVE_FROM_NOT_TODAY_OR_PAST", new[] { "2026-11-10" });
            Assert.Equal(before, await TrailAsync(jonas));
        }
    }

    /// <summary>
    /// <b>F (CI)</b> (ruling 16). A live row from Monday 6 April 2026 (version 1); <c>effectiveFrom</c> Monday 2 March 2026
    /// with <c>If-Match: "1"</c>: 400, <c>fields</c> exactly
    /// <c>[{ EffectiveFrom, BEFORE_CURRENT_ROW, nearestValid ["2026-04-06"] }]</c>; nothing written.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the set-up's PUT answers 404.</para>
    /// </summary>
    [Fact]
    public async Task BeforeCurrentRow_400_NamesItsStart()
    {
        var jonas = await SeedPersonWithLiveRowAsync("2026-04-06", "OVERTIME", true);
        var before = await TrailAsync(jonas);

        using var rsp = await PutAsync(LiseClient(), jonas,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "EXEMPT", onFlexScheme = false }, ifMatch: "1");

        await AssertFieldRefusalAsync(rsp, "EffectiveFrom", "BEFORE_CURRENT_ROW", new[] { "2026-04-06" });
        Assert.Equal(before, await TrailAsync(jonas));
    }

    /// <summary>
    /// <b>F (CI)</b> (ruling 22; consult row 20). A live row from Monday 6 April 2026 holding <c>OVERTIME</c> and flex true
    /// (version 1); the same two values dated today (a later date) with <c>If-Match: "1"</c>: 200 with the open row's
    /// state and the ETag <c>"1"</c>; no second row and nothing else written. A write of <c>SUPERSEDED</c> would add a row
    /// and an audit row, and <c>Assert.Equal(before, after)</c> would fail.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the set-up's PUT answers 404.</para>
    /// </summary>
    [Fact]
    public async Task EqualValues_200_NothingWritten()
    {
        var jonas = await SeedPersonWithLiveRowAsync("2026-04-06", "OVERTIME", true);
        var before = await TrailAsync(jonas);

        using var rsp = await PutAsync(LiseClient(), jonas,
            new { effectiveFrom = "2026-11-10", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifMatch: "1");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("\"1\"", rsp.Headers.ETag?.Tag);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1L, body.GetProperty("version").GetInt64());
        Assert.Equal("2026-04-06", body.GetProperty("effectiveFrom").GetString());
        var row = Assert.Single(await ReadRowsAsync(jonas));
        Assert.Equal((new DateOnly(2026, 4, 6), (DateOnly?)null, 1L), (row.From, row.To, row.Version));
        Assert.Equal(before, await TrailAsync(jonas));
    }

    /// <summary>
    /// <b>F (CI)</b> (T-BC-4, the AC-C13o pattern). A second connection holds Jonas's <c>users</c> row; the PUT from Lise
    /// (STY01 only) waits (polled in <c>pg_stat_activity</c>); the second connection commits
    /// <c>primary_org_id = 'STY02'</c> with the version bumped; the PUT answers 409 with exactly
    /// <c>{ "error": "The user's organisation changed while this request was being processed; refresh and retry." }</c> and
    /// writes nothing.
    /// <para><b>Waits in</b> <c>SELECT * FROM users WHERE user_id = … FOR UPDATE</c>.</para>
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404, so the request never waits and the
    /// helper fails naming it.</para>
    /// </summary>
    [Fact]
    public async Task OrganisationChangedUnderTheLock_409_NothingWritten()
    {
        var jonas = await SeedPersonAsync(Cs, "STY01", "HK");
        var before = await TrailAsync(jonas, withUsersVersion: false);

        var rsp = await RaceUsersRowAsync(jonas,
            "UPDATE users SET primary_org_id = 'STY02', version = version + 1 WHERE user_id = @u",
            () => PutRequest(jonas, new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true));

        await AssertDriftAsync(rsp);
        Assert.Equal("STY02", await ScalarStringAsync(Cs, "SELECT primary_org_id FROM users WHERE user_id = @u", ("u", jonas)));
        Assert.Equal(before, await TrailAsync(jonas, withUsersVersion: false));
    }

    /// <summary>
    /// <b>F (CI)</b> (consult row 12). The same seam with a stale <c>If-Match: "9"</c> on a person with no row: still the
    /// drift 409 (the guard is before any facts row is read), not 412.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> 404 (the helper fails naming it). If the facts rows were read
    /// before the guard the answer would be 412.</para>
    /// </summary>
    [Fact]
    public async Task OrganisationChangedUnderTheLock_StaleIfMatch_StillDrift409()
    {
        var jonas = await SeedPersonAsync(Cs, "STY01", "HK");
        var before = await TrailAsync(jonas, withUsersVersion: false);

        var rsp = await RaceUsersRowAsync(jonas,
            "UPDATE users SET primary_org_id = 'STY02', version = version + 1 WHERE user_id = @u",
            () => PutRequest(jonas, new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifMatch: "9"));

        await AssertDriftAsync(rsp);
        Assert.Equal(before, await TrailAsync(jonas, withUsersVersion: false));
    }

    /// <summary>
    /// <b>F (CI)</b> (consult row 13). The person is deactivated between the scope check and the lock (the same seam: the
    /// second connection sets <c>is_active = false</c> and commits): 404 <c>{ "error": "Employee not found" }</c>,
    /// nothing written.
    /// <para><b>Waits in</b> <c>SELECT * FROM users … FOR UPDATE</c>; the locked read filters <c>is_active</c>, so once
    /// the side commit lands it finds no row.</para>
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> 404 without waiting (the helper fails naming it). A locked
    /// read that ignored <c>is_active</c> would go on to write (200).</para>
    /// </summary>
    [Fact]
    public async Task PersonDeactivatedUnderTheLock_404_NothingWritten()
    {
        var jonas = await SeedPersonAsync(Cs, "STY01", "HK");
        var before = await TrailAsync(jonas, withUsersVersion: false);

        var rsp = await RaceUsersRowAsync(jonas,
            "UPDATE users SET is_active = FALSE, version = version + 1 WHERE user_id = @u",
            () => PutRequest(jonas, new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifNoneMatchStar: true));

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.NotFound, $"expected 404, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(new[] { "error" }, doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("Employee not found", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(before, await TrailAsync(jonas, withUsersVersion: false));
    }

    /// <summary>
    /// <b>F (CI)</b> (consult row 18). Neither <c>If-None-Match</c> nor <c>If-Match</c>: 428; both headers: 428; each
    /// writes nothing.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404.</para>
    /// </summary>
    [Fact]
    public async Task NoPrecondition_428()
    {
        var jonas = await SeedPersonAsync(Cs, "STY01", "HK");
        var before = await TrailAsync(jonas);
        var body = new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true };

        using var neither = await PutAsync(LiseClient(), jonas, body);
        Assert.Equal((HttpStatusCode)428, neither.StatusCode);

        var both = PutRequest(jonas, body, ifMatch: "1", ifNoneMatchStar: true);
        using var bothRsp = await LiseClient().SendAsync(both);
        Assert.Equal((HttpStatusCode)428, bothRsp.StatusCode);

        Assert.Equal(before, await TrailAsync(jonas));
    }

    /// <summary>
    /// <b>F (CI)</b> (consult row 19). A live row at version 1 and <c>If-Match: "7"</c>: 412. And, on a person with no row,
    /// <c>If-Match: "1"</c>: 412. Nothing written by either.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404.</para>
    /// </summary>
    [Fact]
    public async Task StaleIfMatch_412()
    {
        var jonas = await SeedPersonWithLiveRowAsync("2026-04-06", "OVERTIME", true);
        var noRow = await SeedPersonAsync(Cs, "STY01", "HK");
        var beforeLive = await TrailAsync(jonas);
        var beforeNoRow = await TrailAsync(noRow);

        using var stale = await PutAsync(LiseClient(), jonas,
            new { effectiveFrom = "2026-11-10", overtimeRegime = "EXEMPT", onFlexScheme = false }, ifMatch: "7");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var onNoRow = await PutAsync(LiseClient(), noRow,
            new { effectiveFrom = "2026-03-02", overtimeRegime = "OVERTIME", onFlexScheme = true }, ifMatch: "1");
        Assert.Equal(HttpStatusCode.PreconditionFailed, onNoRow.StatusCode);

        Assert.Equal(beforeLive, await TrailAsync(jonas));
        Assert.Equal(beforeNoRow, await TrailAsync(noRow));
    }

    /// <summary>
    /// <b>F (CI)</b> (consult row 19). A live row and <c>If-None-Match: *</c>: 409
    /// <c>{ "error": "HR facts already exist", "currentVersion": 1 }</c> exactly; nothing written.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the set-up's PUT answers 404.</para>
    /// </summary>
    [Fact]
    public async Task CreateOnExisting_409()
    {
        var jonas = await SeedPersonWithLiveRowAsync("2026-04-06", "OVERTIME", true);
        var before = await TrailAsync(jonas);

        using var rsp = await PutAsync(LiseClient(), jonas,
            new { effectiveFrom = "2026-11-10", overtimeRegime = "EXEMPT", onFlexScheme = false }, ifNoneMatchStar: true);

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Conflict, $"expected 409, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(new[] { "currentVersion", "error" },
            doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal("HR facts already exist", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(1L, doc.RootElement.GetProperty("currentVersion").GetInt64());
        Assert.Equal(before, await TrailAsync(jonas));
    }

    /// <summary>
    /// <b>F (CI)</b> (consult row 22). <c>GET …/hr-facts?asOf=not-a-date</c>: 400.
    /// <para><b>Red conditions (F).</b> On <c>1cc0cb1d</c> the route answers 404.</para>
    /// </summary>
    [Fact]
    public async Task GetMalformedAsOf_400()
    {
        var jonas = await SeedPersonAsync(Cs, "STY01", "HK");

        using var rsp = await LiseClient().GetAsync($"/api/admin/employees/{jonas}/hr-facts?asOf=not-a-date");

        Assert.Equal(HttpStatusCode.BadRequest, rsp.StatusCode);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════════════════

    private HttpClient LiseClient() => LocalHrClient(_host, Lise, "STY01");

    /// <summary>A token with the given primary role and exactly these scopes (home organisation = the first scope's).</summary>
    private HttpClient ClientWith(string actor, string role, params RoleScope[] scopes)
    {
        var client = _host.CreateClient();
        var token = TokenService().GenerateToken(
            employeeId: actor, name: actor, role: role, agreementCode: "HK", orgId: scopes[0].OrgId, scopes: scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpRequestMessage PutRequest(string person, object body, string? ifMatch = null, bool ifNoneMatchStar = false)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/employees/{person}/hr-facts")
        {
            Content = JsonContent.Create(body),
        };
        if (ifMatch is not null)
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        if (ifNoneMatchStar)
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return req;
    }

    private Task<HttpResponseMessage> PutAsync(
        HttpClient client, string person, object body, string? ifMatch = null, bool ifNoneMatchStar = false)
        => client.SendAsync(PutRequest(person, body, ifMatch, ifNoneMatchStar));

    private async Task<JsonElement> GetFactsAsync(HttpClient client, string person, string asOf)
    {
        using var rsp = await client.GetAsync($"/api/admin/employees/{person}/hr-facts?asOf={asOf}");
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"GET as of {asOf}: expected 200, got {(int)rsp.StatusCode}: {raw}");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    private static void AssertHkAgreement(JsonElement body)
    {
        Assert.Equal("HK", body.GetProperty("agreementCode").GetString());
        Assert.Equal(new[] { "OVERTIME", "EXEMPT" },
            body.GetProperty("allowedOvertimeRegimes").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>A person in STY01 (HK) with one live row, recorded through the route by Lise (version 1).</summary>
    private async Task<string> SeedPersonWithLiveRowAsync(string effectiveFrom, string regime, bool flex)
    {
        var person = await SeedPersonAsync(Cs, "STY01", "HK");
        using var rsp = await PutAsync(LiseClient(), person,
            new { effectiveFrom, overtimeRegime = regime, onFlexScheme = flex }, ifNoneMatchStar: true);
        Assert.True(rsp.StatusCode == HttpStatusCode.OK,
            $"set-up record: expected 200, got {(int)rsp.StatusCode}: {await rsp.Content.ReadAsStringAsync()}");
        return person;
    }

    /// <summary>403 with exactly the members <c>error</c> ("Access denied") and <c>reason</c> (the house body of the scope and self refusals; a policy 403
    /// has no such body and is asserted by status only, in BelowPolicyRoles_403).</summary>
    private static async Task AssertDeniedAsync(HttpResponseMessage rsp, string reason)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden, $"expected 403, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(new[] { "error", "reason" },
            doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal(reason, doc.RootElement.GetProperty("reason").GetString());
    }

    /// <summary>400 with <c>error</c> "HR facts validation failed" and exactly one field error of this field and code.</summary>
    private static async Task AssertFieldRefusalAsync(HttpResponseMessage rsp, string field, string code, string[]? nearestValid)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.BadRequest, $"expected 400, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("HR facts validation failed", doc.RootElement.GetProperty("error").GetString());
        var one = Assert.Single(doc.RootElement.GetProperty("fields").EnumerateArray().ToList());
        Assert.Equal(field, one.GetProperty("field").GetString());
        Assert.Equal(code, one.GetProperty("code").GetString());
        if (nearestValid is null)
        {
            Assert.True(!one.TryGetProperty("nearestValid", out var nv) || nv.ValueKind == JsonValueKind.Null);
        }
        else
        {
            Assert.Equal(nearestValid,
                one.GetProperty("nearestValid").EnumerateArray().Select(e => e.GetString()).ToArray());
        }
    }

    private static async Task AssertDriftAsync(HttpResponseMessage rsp)
    {
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.Conflict, $"expected 409, got {(int)rsp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(new[] { "error" }, doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(DriftError, doc.RootElement.GetProperty("error").GetString());
    }

    private async Task AssertProjectionAsync(Guid eventId, string person)
    {
        Assert.Equal("TENANT_TARGETED",
            await ScalarStringAsync(Cs, "SELECT visibility_scope FROM audit_projection WHERE event_id = @e", ("e", eventId)));
        Assert.Equal("STY01", await ProjectionTargetOrgAsync(Cs, eventId));
        Assert.Equal(person, await ProjectionTargetResourceAsync(Cs, eventId));
    }

    /// <summary>Hold the person's users row on a second connection, send the request, wait until it waits on a lock in a
    /// <c>FROM users … FOR UPDATE</c> statement, run <paramref name="sideSql"/> on the holder, commit, await the answer.</summary>
    private async Task<HttpResponseMessage> RaceUsersRowAsync(string person, string sideSql, Func<HttpRequestMessage> build)
    {
        await using var side = new NpgsqlConnection(Cs);
        await side.OpenAsync();
        await using var sideTx = await side.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand("SELECT 1 FROM users WHERE user_id = @u FOR UPDATE", side, sideTx))
        {
            lockCmd.Parameters.AddWithValue("u", person);
            await lockCmd.ExecuteScalarAsync();
        }

        var requestTask = LiseClient().SendAsync(build());

        var observed = await PriorMonthExportRaceTests.WaitUntilAsync(async () =>
        {
            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity
                    WHERE pid <> pg_backend_pid()
                      AND datname = current_database()
                      AND wait_event_type = 'Lock'
                      AND query ILIKE '%FROM users%FOR UPDATE%'
                )
                """, conn);
            return await cmd.ExecuteScalarAsync() is true;
        }, TimeSpan.FromSeconds(30), () => requestTask.IsCompleted);

        if (!observed || requestTask.IsCompleted)
        {
            await sideTx.RollbackAsync();
            var finished = await Task.WhenAny(requestTask, Task.Delay(TimeSpan.FromSeconds(30))) == requestTask;
            if (!finished)
                Assert.Fail("no backend was seen waiting in a users ... FOR UPDATE statement within the timeout; the lock was released and the request was still pending after a further 30 seconds.");
            var early = await requestTask;
            Assert.Fail($"the request answered {(int)early.StatusCode} ({await early.Content.ReadAsStringAsync()}) without being held in a users ... FOR UPDATE statement; it never reached the race window.");
        }

        // CA2100-justified (QUAL-073 ratchet): the side statement is a compile-time constant at every call site; the value goes through a parameter.
#pragma warning disable CA2100
        await using (var sideCmd = new NpgsqlCommand(sideSql, side, sideTx))
#pragma warning restore CA2100
        {
            sideCmd.Parameters.AddWithValue("u", person);
            Assert.Equal(1, await sideCmd.ExecuteNonQueryAsync());
        }
        await sideTx.CommitAsync();
        return await requestTask;
    }

    private sealed record FactsRow(Guid FactsId, string? Regime, bool? Flex, DateOnly From, DateOnly? To, long Version);

    private sealed record AuditRow(string Action, long? VersionBefore, long VersionAfter, string ActorId);

    private async Task<IReadOnlyList<FactsRow>> ReadRowsAsync(string person)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT facts_id, overtime_regime, on_flex_scheme, effective_from, effective_to, version
            FROM employee_hr_facts WHERE employee_id = @u ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("u", person);
        var rows = new List<FactsRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new FactsRow(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetBoolean(2),
                reader.GetFieldValue<DateOnly>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4),
                reader.GetInt64(5)));
        }
        return rows;
    }

    private async Task<IReadOnlyList<AuditRow>> ReadAuditAsync(string person)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after, actor_id
            FROM employee_hr_facts_audit WHERE employee_id = @u ORDER BY audit_id
            """, conn);
        cmd.Parameters.AddWithValue("u", person);
        var rows = new List<AuditRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new AuditRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3)));
        return rows;
    }

    private async Task<IReadOnlyList<EventRow>> ReadEventsAsync(string person)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id, event_payload::text FROM outbox_events
            WHERE stream_id = @s AND event_type = 'EmployeeHrFactsChanged' ORDER BY outbox_id
            """, conn);
        cmd.Parameters.AddWithValue("s", $"employee-hr-facts-{person}");
        var rows = new List<EventRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new EventRow(reader.GetGuid(0), JsonSerializer.Deserialize<JsonElement>(reader.GetString(1))));
        return rows;
    }

    /// <summary>Everything a refused request must leave alone, filtered to the person, as one comparable string: the
    /// facts rows, the facts-audit rows, the events on the person's stream, the audit-view rows about them and the
    /// users row's version.</summary>
    private Task<string?> TrailAsync(string person, bool withUsersVersion = true) => ScalarStringAsync(Cs,
        """
        SELECT concat_ws('|',
          (SELECT COALESCE(string_agg(f::text, ';' ORDER BY f.effective_from), '')
             FROM employee_hr_facts f WHERE f.employee_id = @u),
          (SELECT COUNT(*) FROM employee_hr_facts_audit WHERE employee_id = @u),
          (SELECT COUNT(*) FROM outbox_events WHERE stream_id = 'employee-hr-facts-' || @u),
          (SELECT COUNT(*) FROM audit_projection WHERE target_resource_id = @u AND event_type = 'EmployeeHrFactsChanged'),
          CASE WHEN @v THEN (SELECT version FROM users WHERE user_id = @u)::text ELSE '' END)
        """, ("u", person), ("v", withUsersVersion));

    private async Task ExecAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant at every call site; values go
        // through parameters, never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── payroll set-up for Edit_CreatesNoPayrollBoundary (the TransferPayrollBoundaryTests shape) ──

    private async Task SeedWeekdayHoursAsync(string person, DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await ExecAsync(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, agreement_code, ok_version,
                     voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES (gen_random_uuid(), @emp, @date, 7.4, 'HK', 'OK26', FALSE, NOW(), @outbox)
                """, ("emp", person), ("date", day), ("outbox", Interlocked.Increment(ref _outboxSeq)));
        }
    }

    private Task SeedApprovedMonthAsync(string person, DateOnly start, DateOnly end) => ExecAsync(
        """
        INSERT INTO approval_periods (
            period_id, employee_id, org_id, period_start, period_end, period_type, status,
            submitted_at, submitted_by, approved_by, approved_at, agreement_code, ok_version)
        VALUES (
            gen_random_uuid(), @emp, 'STY01', @start, @end, 'MONTHLY', 'APPROVED',
            NOW(), @emp, 'ROOT-S148', NOW(), 'HK', 'OK26')
        """, ("emp", person), ("start", start), ("end", end));

    private static async Task<HttpResponseMessage> CalculateAndExportAsync(
        PayrollHostFactory payroll, string person, DateOnly start, DateOnly end)
    {
        var body = new
        {
            profile = new EmploymentProfile
            {
                EmployeeId = person,
                AgreementCode = "HK",
                OkVersion = "OK26",
                EmploymentCategory = "Standard",
                PartTimeFraction = 1.0m,
                OrgId = "STY01",
            },
            periodStart = start,
            periodEnd = end,
        };
        // The delivering host (as TransferPayrollBoundaryTests): after a committed export payroll posts to mock payroll.
        var client = payroll.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new DeliveringClientFactory());
        })).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(body),
        };
        var token = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = PayrollHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        }).GenerateToken(
            employeeId: "ROOT-S148", name: "ROOT-S148", role: StatsTidRoles.GlobalAdmin, agreementCode: "AC",
            orgId: null, scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private sealed class DeliveringClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler = new TestFixtures.StubHandler(request =>
            request.Method == HttpMethod.Post
            && string.Equals(request.RequestUri?.AbsolutePath, "/api/payroll/receive", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : TestFixtures.DefaultRuleEngineHandler(request));

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed record Manifest(string Segments, string CauseSummary);

    private async Task<IReadOnlyList<Manifest>> ReadManifestsAsync(string person)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT segments_jsonb::text, array_to_string(boundary_cause_summary, ',')
            FROM segment_manifests WHERE employee_id = @e ORDER BY created_at
            """, conn);
        cmd.Parameters.AddWithValue("e", person);
        var rows = new List<Manifest>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new Manifest(reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        return rows;
    }
}
