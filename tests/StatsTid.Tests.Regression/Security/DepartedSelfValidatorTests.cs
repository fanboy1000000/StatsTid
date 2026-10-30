using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// One Postgres container (the real <c>docker/postgres/init.sql</c>, which seeds the STY01 / STY05
/// organisations) shared by <see cref="DepartedSelfValidatorTests"/>. Isolation is by case-unique ids.
/// </summary>
public sealed class DepartedSelfDbFixture : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}

/// <summary>
/// S147 / TASK-14703-T (AC-230-16; R-30; refinement <c>:786-794</c>) — the validator's departed-self
/// clause, on BOTH employee overloads, over a SEEDED subject (still <c>is_active = TRUE</c> with an
/// end date in the past: the real grace state, not a mock).
///
/// <para><b>Docker.</b> Report as CI-pending until CI-W2 shows them green; they cannot run locally.
/// The clock is a <see cref="FixedTimeProvider"/> pinned to <see cref="Today"/> (Monday 2026-03-16,
/// Copenhagen day), so every date is relative to one fixed "today" and no fact depends on the
/// real calendar day (PAT-008).</para>
///
/// <para><b>Actor tier.</b> The self-actor is an HR-tier actor (LocalHR) whose scope COVERS the
/// subject's organisation. That is what makes the self-deny facts discriminating: an Employee-only
/// actor returns from the own-data branch before the clause, and a non-covering actor would be
/// refused by scope for the wrong reason. The reason asserted is
/// <see cref="DepartedSubject.DenyReason"/> (the clause), never the HR gate's or the scope loop's.</para>
///
/// <para><b>Kinds.</b> "Deny" facts are <b>F</b> (they assert <c>Allowed == false</c> with the clause's
/// reason; today's validator admits the covered HR self). "Allow" and "not found" facts are <b>B</b>
/// (boundary pin — holds today; they keep the clause from over-reaching). Mutations for the wave gate:
/// M-15 drops the clause from the active-only overload; M-16 drops it from the terminated-inclusive
/// overload; M-17 moves it AFTER the HR gate; M-11 changes <c>&lt;</c> to <c>&lt;=</c> in the predicate.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DepartedSelfValidatorTests : IClassFixture<DepartedSelfDbFixture>
{
    private const string Org = "STY01";
    private static readonly DateOnly Today = new(2026, 3, 16);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    private readonly DepartedSelfDbFixture _fx;
    public DepartedSelfValidatorTests(DepartedSelfDbFixture fx) => _fx = fx;

    private OrgScopeValidator Validator()
    {
        var factory = new DbConnectionFactory(_fx.ConnectionString);
        return new OrgScopeValidator(
            new OrganizationRepository(factory),
            new UserRepository(factory),
            new FixedTimeProvider(Today),
            NullLogger<OrgScopeValidator>.Instance);
    }

    private static ActorContext Hr(string id) => new(
        id, StatsTidRoles.LocalHR, Guid.NewGuid(), Org,
        new[] { new RoleScope(StatsTidRoles.LocalHR, Org, "ORG_ONLY") });

    private static ActorContext Leader(string id) => new(
        id, StatsTidRoles.LocalLeader, Guid.NewGuid(), Org,
        new[] { new RoleScope(StatsTidRoles.LocalLeader, Org, "ORG_ONLY") });

    private async Task<string> SeedAsync(string prefix, bool active, DateOnly? end)
    {
        var id = prefix + "_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_fx.ConnectionString, id, Org, "AC", "OK24");
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = @a, employment_end_date = @e, updated_at = NOW() WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("a", active);
        cmd.Parameters.AddWithValue("e", (object?)end ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", id);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        return id;
    }

    // ── self, active, end date yesterday (the grace state) -> DENY, both overloads (F) ──

    /// <summary>Kind F. Red condition: <c>Assert.False(Allowed)</c> — today the HR self whose scope covers
    /// the org is admitted by the active-only overload (the target read succeeds: still active).</summary>
    [Fact]
    public async Task ActiveOnly_SelfActive_EndDateYesterday_Denied_WithClauseReason()
    {
        var self = await SeedAsync("ds_v_a1", active: true, end: Yesterday);
        var r = await Validator().ValidateEmployeeAccessAsync(Hr(self), self);
        Assert.False(r.Allowed);
        Assert.Equal(DepartedSubject.DenyReason, r.Reason);
    }

    /// <summary>Kind F. Red condition: <c>Assert.False(Allowed)</c> — today the terminated-inclusive
    /// overload admits the covered HR self (an ACTIVE target never meets the HR gate's refusal).</summary>
    [Fact]
    public async Task TerminatedInclusive_SelfActive_EndDateYesterday_Denied_WithClauseReason()
    {
        var self = await SeedAsync("ds_v_t1", active: true, end: Yesterday);
        var r = await Validator().ValidateEmployeeAccessIncludingTerminatedAsync(Hr(self), self);
        Assert.False(r.Allowed);
        Assert.Equal(DepartedSubject.DenyReason, r.Reason);
    }

    /// <summary>Kind F, the role-floored active-only overload (the leader-arm reopen's call, floor LocalHR).
    /// Red condition: <c>Assert.False(Allowed)</c> (today admitted).</summary>
    [Fact]
    public async Task ActiveOnly_RoleFloored_SelfActive_EndDateYesterday_Denied()
    {
        var self = await SeedAsync("ds_v_a1f", active: true, end: Yesterday);
        var r = await Validator().ValidateEmployeeAccessAsync(Hr(self), self, StatsTidRoles.LocalHR);
        Assert.False(r.Allowed);
        Assert.Equal(DepartedSubject.DenyReason, r.Reason);
    }

    // ── self, end date TODAY -> ALLOW (day D is employed), both overloads (B; mutation M-11) ──

    /// <summary>Kind B — boundary pin, holds today: the last day is still employed (mutation M-11 turns it
    /// RED). Red condition (under the mutation): <c>Assert.True(Allowed)</c>.</summary>
    [Fact]
    public async Task ActiveOnly_SelfActive_EndDateToday_Allowed()
    {
        var self = await SeedAsync("ds_v_a2", active: true, end: Today);
        Assert.True((await Validator().ValidateEmployeeAccessAsync(Hr(self), self)).Allowed);
    }

    /// <summary>Kind B (M-11), terminated-inclusive overload.</summary>
    [Fact]
    public async Task TerminatedInclusive_SelfActive_EndDateToday_Allowed()
    {
        var self = await SeedAsync("ds_v_t2", active: true, end: Today);
        Assert.True((await Validator().ValidateEmployeeAccessIncludingTerminatedAsync(Hr(self), self)).Allowed);
    }

    // ── self, no end date -> ALLOW (B) ──

    /// <summary>Kind B — boundary pin, holds today: open-ended employment is not departed.</summary>
    [Fact]
    public async Task ActiveOnly_SelfActive_NoEndDate_Allowed()
    {
        var self = await SeedAsync("ds_v_a3", active: true, end: null);
        Assert.True((await Validator().ValidateEmployeeAccessAsync(Hr(self), self)).Allowed);
    }

    /// <summary>Kind B — boundary pin, holds today: terminated-inclusive overload, no end date.</summary>
    [Fact]
    public async Task TerminatedInclusive_SelfActive_NoEndDate_Allowed()
    {
        var self = await SeedAsync("ds_v_t3", active: true, end: null);
        Assert.True((await Validator().ValidateEmployeeAccessIncludingTerminatedAsync(Hr(self), self)).Allowed);
    }

    // ── self, deactivated ──

    /// <summary>Kind B — boundary pin, holds today: the active-only overload never resolves a deactivated
    /// target, so a deactivated self is "Target employee not found" (as today; the clause is not reached).</summary>
    [Fact]
    public async Task ActiveOnly_SelfDeactivated_TargetNotFound_AsToday()
    {
        var self = await SeedAsync("ds_v_a4", active: false, end: Yesterday);
        var r = await Validator().ValidateEmployeeAccessAsync(Hr(self), self);
        Assert.False(r.Allowed);
        Assert.Equal("Target employee not found", r.Reason);
    }

    /// <summary>Kind F. HR-TIER actor (LocalHR covering the org): before S147 the HR gate and the scope loop
    /// ADMIT a deactivated HR self (the S136 residual); now the clause refuses it. Red condition:
    /// <c>Assert.False(Allowed)</c> (today admitted) and the reason equals
    /// <see cref="DepartedSubject.DenyReason"/>, not the HR gate's reason.</summary>
    [Fact]
    public async Task TerminatedInclusive_SelfDeactivated_HrTier_DeniedByClause()
    {
        var self = await SeedAsync("ds_v_t4", active: false, end: Yesterday);
        var r = await Validator().ValidateEmployeeAccessIncludingTerminatedAsync(Hr(self), self);
        Assert.False(r.Allowed);
        Assert.Equal(DepartedSubject.DenyReason, r.Reason);
    }

    /// <summary>Kind F, ordering (mutation M-17, clause after the HR gate). A deactivated NON-HR self (a
    /// LocalLeader) is refused by the clause BEFORE the HR gate, so the reason is the clause's. Red
    /// condition: today this actor is refused with the HR gate's reason, so
    /// <c>Assert.Equal(DenyReason, Reason)</c> fails.</summary>
    [Fact]
    public async Task TerminatedInclusive_SelfDeactivated_Leader_ReasonIsClause_NotHrGate()
    {
        var self = await SeedAsync("ds_v_t4l", active: false, end: Yesterday);
        var r = await Validator().ValidateEmployeeAccessIncludingTerminatedAsync(Leader(self), self);
        Assert.False(r.Allowed);
        Assert.Equal(DepartedSubject.DenyReason, r.Reason);
    }

    // ── another person ──

    /// <summary>Kind B — boundary pin, holds today (the grace; OQ-2): a LocalLeader reaches ANOTHER person
    /// who is still active with an end date in the past. Both overloads.</summary>
    [Fact]
    public async Task Other_ActiveEndDatePassed_LeaderScope_Allowed_BothOverloads()
    {
        var subject = await SeedAsync("ds_v_o1", active: true, end: Yesterday);
        var leader = Leader("ds_v_o1_ldr_" + Guid.NewGuid().ToString("N")[..6]);
        var v = Validator();
        Assert.True((await v.ValidateEmployeeAccessAsync(leader, subject)).Allowed);
        Assert.True((await v.ValidateEmployeeAccessIncludingTerminatedAsync(leader, subject)).Allowed);
    }

    /// <summary>Kind B — boundary pin, holds today: HR reaches ANOTHER person who is deactivated, on the
    /// terminated-inclusive overload.</summary>
    [Fact]
    public async Task Other_Deactivated_HrScope_TerminatedInclusive_Allowed()
    {
        var subject = await SeedAsync("ds_v_o2", active: false, end: Yesterday);
        var hr = Hr("ds_v_o2_hr_" + Guid.NewGuid().ToString("N")[..6]);
        Assert.True((await Validator().ValidateEmployeeAccessIncludingTerminatedAsync(hr, subject)).Allowed);
    }

    /// <summary>Kind B — boundary pin, holds today: on the ACTIVE-ONLY overload, another person who is
    /// deactivated is "Target employee not found" (the brief's "another person, deactivated, HR scope ->
    /// allow" holds on the terminated-inclusive overload only).</summary>
    [Fact]
    public async Task Other_Deactivated_HrScope_ActiveOnly_NotFound_AsToday()
    {
        var subject = await SeedAsync("ds_v_o3", active: false, end: Yesterday);
        var hr = Hr("ds_v_o3_hr_" + Guid.NewGuid().ToString("N")[..6]);
        var r = await Validator().ValidateEmployeeAccessAsync(hr, subject);
        Assert.False(r.Allowed);
        Assert.Equal("Target employee not found", r.Reason);
    }

    // ── G1: an actor with no id is never "self" ──

    /// <summary>Kind B (G1) — boundary pin, holds today: an HR-scoped actor with NO actor id acting on a
    /// person past their end date is admitted as an ordinary other person (the grace), on both overloads;
    /// an id-less token is never anybody's self.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ActorWithoutId_IsNeverSelf_AdmittedAsOtherPerson(string? actorId)
    {
        var subject = await SeedAsync("ds_v_g1", active: true, end: Yesterday);
        var actor = Hr("placeholder") with { ActorId = actorId };
        var v = Validator();
        Assert.True((await v.ValidateEmployeeAccessAsync(actor, subject)).Allowed);
        Assert.True((await v.ValidateEmployeeAccessIncludingTerminatedAsync(actor, subject)).Allowed);
    }

    // ── G2: an Employee-only actor past their end date: the clause is not reached ──

    /// <summary>Kind B (G2) — boundary pin, holds today: an Employee-ONLY actor acting on their own record is
    /// admitted by the own-data branch, which returns before the clause. The self-service floor
    /// (TASK-14710) is what refuses such an Employee at the screens they use; this pin says the VALIDATOR
    /// does not, so nobody moves the clause into the own-data branch.</summary>
    [Fact]
    public async Task ActiveOnly_EmployeeOnlySelf_PastEndDate_OwnDataBranch_AsToday_FloorIsTask14710()
    {
        var self = await SeedAsync("ds_v_g2", active: true, end: Yesterday);
        var emp = new ActorContext(
            self, StatsTidRoles.Employee, Guid.NewGuid(), Org,
            new[] { new RoleScope(StatsTidRoles.Employee, Org, "ORG_ONLY") });
        Assert.True((await Validator().ValidateEmployeeAccessAsync(emp, self)).Allowed);
    }
}
