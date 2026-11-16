using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.Hosting;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14818-T — the shared Docker fixture of the dated-configuration facts: a fresh database with the
/// full schema, a Backend host whose clock a fact can pin (a derived host per clock), and the small set of
/// helpers every fact of the family needs (a valid agreement-config body, a publish with or without a date,
/// the 400 refusal check, a scalar read). Every fact builds its own world on this fixture and uses a
/// test-unique agreement code (or a test-unique position under HK / OK26), so none reads a seeded published
/// configuration (NR-P15, NR-P24, Fable c3 N-2).
///
/// <para>
/// <b>Clocks.</b> A derived host (<c>WithFixedToday</c> / <c>WithFixedInstant</c>) re-runs the startup seeders
/// on its first <c>CreateClient()</c> against the same database (the boot-order rule on
/// <see cref="StatsTidWebApplicationFactory.WithFixedToday"/>); every fact here seeds only through the routes
/// or the repositories after that call, so the rule holds. Docker-gated: CI-verified, never locally green.
/// </para>
/// </summary>
public abstract class DatedConfigDockerFixture : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>The OK version of the facts' test-unique agreement codes.</summary>
    protected const string Ok26 = "OK26";

    private readonly List<WebApplicationFactory<Program>> _hosts = new();

    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    /// <summary>The fresh database's connection string.</summary>
    protected string ConnectionString => _harness.ConnectionString;

    /// <summary>The fresh database's connection factory (the repositories' and the in-transaction writers').</summary>
    protected DbConnectionFactory Db => _harness.Factory;
    protected StatsTidWebApplicationFactory Factory { get; private set; } = null!;
    protected AgreementConfigRepository ConfigRepo { get; private set; } = null!;
    protected PositionOverrideRepository OverrideRepo { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(ConnectionString);
        Factory = new StatsTidWebApplicationFactory(ConnectionString);
        ConfigRepo = new AgreementConfigRepository(Db);
        OverrideRepo = new PositionOverrideRepository(Db);
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
            host.Dispose();
        Factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ── Hosts and clients ─────────────────────────────────────────────────────────────────────

    /// <summary>A GlobalAdmin client on a host whose clock is pinned to UTC midnight of <paramref name="today"/>.</summary>
    protected HttpClient AdminAt(DateOnly today) => AdminOn(Factory.WithFixedToday(today));

    /// <summary>A GlobalAdmin client on a host whose clock is pinned to the exact <paramref name="instant"/>.</summary>
    protected HttpClient AdminAtInstant(DateTimeOffset instant) => AdminOn(Factory.WithFixedInstant(instant));

    private HttpClient AdminOn(WebApplicationFactory<Program> host)
    {
        _hosts.Add(host);
        var client = host.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: "s148_dated_gadmin", name: "s148_dated_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: "S148DATED",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ── Agreement-config helpers ──────────────────────────────────────────────────────────────

    protected static string UniqueCode(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>A validation-passing agreement-config request body; the members a fact varies are parameters.</summary>
    protected static object ConfigBody(
        string agreementCode, string okVersion = Ok26, decimal weeklyNorm = 37m, bool hasOvertime = true,
        bool hasMerarbejde = false, decimal overtimeThreshold50 = 37m, decimal maxFlexBalance = 100m) => new
    {
        agreementCode,
        okVersion,
        description = "S148 dated configuration fact",
        normModel = "WEEKLY_HOURS",
        weeklyNormHours = weeklyNorm,
        normPeriodWeeks = 1,
        annualNormHours = 1924m,
        maxFlexBalance,
        hasOvertime,
        hasMerarbejde,
        overtimeThreshold50,
        overtimeThreshold100 = 44m,
        eveningSupplementEnabled = false,
        nightSupplementEnabled = false,
        weekendSupplementEnabled = false,
        holidaySupplementEnabled = false,
        eveningStart = 17,
        eveningEnd = 23,
        nightStart = 23,
        nightEnd = 6,
        eveningRate = 1.25m,
        nightRate = 1.5m,
        weekendSaturdayRate = 1.5m,
        weekendSundayRate = 2m,
        holidayRate = 2m,
        onCallDutyEnabled = false,
        onCallDutyRate = 0.33m,
        callInWorkEnabled = false,
        callInMinimumHours = 3m,
        callInRate = 1m,
        travelTimeEnabled = false,
        workingTravelRate = 1m,
        nonWorkingTravelRate = 0.5m,
        maxDailyHours = 13m,
        minimumRestHours = 11m,
        restPeriodDerogationAllowed = false,
        weeklyMaxHoursReferencePeriod = 17,
        voluntaryUnsocialHoursAllowed = true,
    };

    /// <summary>Creates a DRAFT through the real create route; returns its id and the ETag version.</summary>
    protected static async Task<(Guid Id, long Version)> CreateDraftAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/agreement-configs", body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"draft create expected 201, got {(int)response.StatusCode}: {text}");
        var id = JsonDocument.Parse(text).RootElement.GetProperty("configId").GetGuid();
        return (id, S118ContractAssert.EtagVersion(response));
    }

    /// <summary>
    /// Publishes a draft. <paramref name="appliesFrom"/> null sends no body at all (the date omitted);
    /// otherwise <c>{"appliesFrom":"yyyy-MM-dd"}</c>.
    /// </summary>
    protected static Task<HttpResponseMessage> PublishAsync(
        HttpClient client, Guid configId, long version, string? appliesFrom = null)
        => client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/agreement-configs/{configId}/publish",
            appliesFrom is null ? null : $$"""{"appliesFrom":"{{appliesFrom}}"}""", version));

    /// <summary>Publishes and asserts 200; returns the parsed 200 body.</summary>
    protected static async Task<JsonElement> PublishOkAsync(
        HttpClient client, Guid configId, long version, string? appliesFrom = null)
    {
        using var response = await PublishAsync(client, configId, version, appliesFrom);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"publish of {configId} (appliesFrom {appliesFrom ?? "omitted"}) expected 200, got {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    /// Create-and-publish through the routes: a draft of <paramref name="body"/> published dated
    /// <paramref name="appliesFrom"/>; returns its id.
    /// </summary>
    protected static async Task<Guid> CreateAndPublishAsync(HttpClient client, object body, string? appliesFrom)
    {
        var (id, version) = await CreateDraftAsync(client, body);
        await PublishOkAsync(client, id, version, appliesFrom);
        return id;
    }

    /// <summary>A repository-level agreement-config entity (a DRAFT once created); the members a fact varies are parameters.</summary>
    protected static AgreementConfigEntity NewConfig(
        string agreementCode, string okVersion = Ok26, decimal weeklyNorm = 37m, decimal maxFlexBalance = 100m) => new()
    {
        ConfigId = Guid.Empty,
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        Status = AgreementConfigStatus.DRAFT,
        WeeklyNormHours = weeklyNorm,
        NormPeriodWeeks = 1,
        NormModel = NormModel.WEEKLY_HOURS,
        AnnualNormHours = 1924m,
        MaxFlexBalance = maxFlexBalance,
        HasOvertime = true,
        HasMerarbejde = false,
        OvertimeThreshold50 = 37m,
        OvertimeThreshold100 = 44m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        EveningStart = 17,
        EveningEnd = 23,
        NightStart = 23,
        NightEnd = 6,
        EveningRate = 1.25m,
        NightRate = 1.5m,
        WeekendSaturdayRate = 1.5m,
        WeekendSundayRate = 2m,
        HolidayRate = 2m,
        OnCallDutyEnabled = false,
        OnCallDutyRate = 0.33m,
        CallInWorkEnabled = false,
        CallInMinimumHours = 3m,
        CallInRate = 1m,
        TravelTimeEnabled = false,
        WorkingTravelRate = 1m,
        NonWorkingTravelRate = 0.5m,
        MaxDailyHours = 13m,
        MinimumRestHours = 11m,
        RestPeriodDerogationAllowed = false,
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
        DefaultCompensationModel = "UDBETALING",
        EmployeeCompensationChoice = false,
        MaxOvertimeHoursPerPeriod = 0m,
        OvertimeRequiresPreApproval = false,
        CreatedBy = "tester",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Description = "S148 dated configuration fact",
    };

    /// <summary>
    /// A repository-level create-then-publish in its own transaction, dated <paramref name="appliesFrom"/>
    /// (the date rules apply as on the route). Returns the new config's id.
    /// </summary>
    protected async Task<Guid> RepoCreateAndPublishAsync(AgreementConfigEntity entity, DateOnly appliesFrom)
    {
        var id = await ConfigRepo.CreateAsync(entity);
        var draft = await ConfigRepo.GetByIdAsync(id) ?? throw new InvalidOperationException("draft vanished");
        await using var conn = Db.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await ConfigRepo.PublishAsync(conn, tx, id, draft.Version, appliesFrom, "tester");
        await tx.CommitAsync();
        return id;
    }

    // ── Position-override helpers ─────────────────────────────────────────────────────────────

    /// <summary>Seeds a test-unique <c>positions</c> row (the override's FK target) and returns its code.</summary>
    protected async Task<string> SeedPositionAsync(string prefix = "POS")
    {
        var code = UniqueCode(prefix);
        await ExecAsync(
            "INSERT INTO positions (position_code, display_label, agreement_code) VALUES (@code, @code, 'HK')",
            ("code", code));
        return code;
    }

    /// <summary>An override write body (create and edit share the member set).</summary>
    protected static object OverrideBody(
        string agreementCode, string okVersion, string positionCode,
        decimal? maxFlexBalance, decimal? weeklyNormHours, string? appliesFrom) => new
    {
        agreementCode,
        okVersion,
        positionCode,
        maxFlexBalance,
        normPeriodWeeks = 4,
        weeklyNormHours,
        description = "S148 dated override fact",
        appliesFrom,
    };

    protected static async Task<(Guid Id, long Version)> CreateOverrideAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/position-overrides", body);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"override create expected 201, got {(int)response.StatusCode}: {text}");
        var id = JsonDocument.Parse(text).RootElement.GetProperty("overrideId").GetGuid();
        return (id, S118ContractAssert.EtagVersion(response));
    }

    protected static async Task<HttpResponseMessage> EditOverrideAsync(
        HttpClient client, Guid overrideId, long version, object body)
    {
        var request = SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Put, $"/api/admin/position-overrides/{overrideId}",
            JsonSerializer.Serialize(body), version);
        return await client.SendAsync(request);
    }

    /// <summary>The deactivate (<paramref name="action"/> = "deactivate") or activate route, dated.</summary>
    protected static async Task<HttpResponseMessage> TransitionOverrideAsync(
        HttpClient client, Guid overrideId, long version, string action, string appliesFrom)
        => await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(
            HttpMethod.Post, $"/api/admin/position-overrides/{overrideId}/{action}",
            $$"""{"appliesFrom":"{{appliesFrom}}"}""", version));

    /// <summary>The ETag version a successful override write answered with.</summary>
    protected static long EtagOf(HttpResponseMessage response) => S118ContractAssert.EtagVersion(response);

    // ── Assertions and reads ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Asserts a 400 whose body is the applies-from refusal with exactly one entry: <paramref name="field"/>,
    /// <paramref name="code"/> and the exact <paramref name="nearestValid"/> list (ISO dates).
    /// </summary>
    protected static async Task AssertRefusalAsync(
        HttpResponseMessage response, string field, string code, params string[] nearestValid)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
            $"expected 400 for {code}, got {(int)response.StatusCode}: {text}");
        var root = JsonDocument.Parse(text).RootElement;
        Assert.Equal("Applies-from date validation failed", root.GetProperty("error").GetString());
        var fields = root.GetProperty("fields");
        Assert.Equal(1, fields.GetArrayLength());
        Assert.Equal(field, fields[0].GetProperty("field").GetString());
        Assert.Equal(code, fields[0].GetProperty("code").GetString());
        Assert.Equal(
            nearestValid,
            fields[0].GetProperty("nearestValid").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

#pragma warning disable CA2100 // each sql is a literal at its call site; values flow through parameters (QUAL-073).
    protected async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    protected Task<long> CountAsync(string sql, params (string Name, object Value)[] args)
        => ScalarAsync<long>(sql, args);

    protected async Task ExecAsync(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

#pragma warning restore CA2100

    protected Task<string?> StatusOfConfigAsync(Guid configId)
        => ScalarAsync<string>("SELECT status FROM agreement_configs WHERE config_id = @id", ("id", configId));

    protected Task<long> PublishedAuditRowsAsync(Guid configId)
        => CountAsync(
            "SELECT COUNT(*) FROM agreement_config_audit WHERE config_id = @id AND action = 'PUBLISHED'",
            ("id", configId));
}

/// <summary>
/// S148 / TASK-14818-T — the applies-from date as the routes enforce it: AC-C2b (1) (a mid-week date for a
/// week-read field), AC-C2e (a future date), AC-C2q (a date before the version it replaces) and AC-C2l (an
/// omitted date means the Copenhagen today). Plain language: an admin may say "from this day", but never a
/// day still to come, never a day before the version being replaced, and a change to the weekly norm or the
/// overtime regime must start on a Monday, because those are read week by week; each refusal offers the
/// nearest valid date. Docker-gated: argued against TASK-14818's base, run in CI (CI-W4a).
/// </summary>
[Trait("Category", "Docker")]
public sealed class ConfigDateValidationTests : DatedConfigDockerFixture
{
    private static readonly DateOnly Monday9Nov2026 = new(2026, 11, 9);
    private static readonly DateOnly Thursday12Nov2026 = new(2026, 11, 12);
    private static readonly DateOnly Monday16Nov2026 = new(2026, 11, 16);

    /// <summary>
    /// AC-C2b (1), four arms over TASK-14818's closed Monday list. Clock Thursday 12 November 2026; v1 (37 h)
    /// published dated Monday 2 November 2026. Arm 1: a DRAFT at 32 h dated Wednesday 11 November is refused,
    /// field <c>WeeklyNormHours</c>, code <c>NOT_MONDAY_ALIGNED</c>, nearest valid Monday 9 and Monday 16
    /// November, and stays a DRAFT with no PUBLISHED audit row. Arm 2: a DRAFT equal to v1 except
    /// <c>HasOvertime</c> (true to false, a value the publish validator accepts) is refused the same way, field
    /// <c>HasOvertime</c>. Arm 3: a DRAFT equal to v1 except <c>OvertimeThreshold50</c> (37 to 38) dated the same
    /// Wednesday is accepted (the list is no wider than ruled). Arm 4, the override: weekly norm 37 created
    /// dated Monday 2 November; an edit to 32 and a deactivation, both dated Wednesday 11 November, are refused
    /// (field <c>WeeklyNormHours</c>), and an edit changing <c>MaxFlexBalance</c> alone (norm still 37) is accepted.
    ///
    /// <para>
    /// <b>Kind: F (CI)</b>, a Docker HTTP fact; no local RED. <b>Red conditions</b> (argued against TASK-14818's
    /// base, <c>e9d09a66</c> plus the schema): the routes ignore the date member and answer 200 for arms 1 and 2
    /// and for arm 4's edit and deactivation, so the 400 assertions fail first. Arms 3 and 4's last step pass on
    /// the base too and pin that the closed list is no wider than ruled. The ruled <c>field</c> for the date's
    /// own errors is <c>AppliesFrom</c> (TASK-14818's report § 3); a Monday error names the changed field.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MidWeekDateForWeekReadField_RefusedWithNearestMondays()
    {
        using var admin = AdminAt(Thursday12Nov2026);
        var code = UniqueCode("K_MIDW_");
        string[] nearestMondays = { "2026-11-09", "2026-11-16" };

        // v1 (37 h), the key's first version, dated Monday 2 November 2026.
        await CreateAndPublishAsync(admin, ConfigBody(code), "2026-11-02");

        // Arm 1 — WeeklyNormHours, mid-week.
        var (norm32, norm32Version) = await CreateDraftAsync(admin, ConfigBody(code, weeklyNorm: 32m));
        using (var refused = await PublishAsync(admin, norm32, norm32Version, "2026-11-11"))
            await AssertRefusalAsync(refused, "WeeklyNormHours", "NOT_MONDAY_ALIGNED", nearestMondays);
        Assert.Equal("DRAFT", await StatusOfConfigAsync(norm32));
        Assert.Equal(0L, await PublishedAuditRowsAsync(norm32));

        // Arm 2 — one regime field (HasOvertime), mid-week.
        var (noOvertime, noOvertimeVersion) = await CreateDraftAsync(admin, ConfigBody(code, hasOvertime: false));
        using (var refused = await PublishAsync(admin, noOvertime, noOvertimeVersion, "2026-11-11"))
            await AssertRefusalAsync(refused, "HasOvertime", "NOT_MONDAY_ALIGNED", nearestMondays);
        Assert.Equal("DRAFT", await StatusOfConfigAsync(noOvertime));
        Assert.Equal(0L, await PublishedAuditRowsAsync(noOvertime));

        // Arm 3 — OvertimeThreshold50 alone, mid-week: accepted.
        var (threshold, thresholdVersion) = await CreateDraftAsync(admin, ConfigBody(code, overtimeThreshold50: 38m));
        await PublishOkAsync(admin, threshold, thresholdVersion, "2026-11-11");
        Assert.Equal("ACTIVE", await StatusOfConfigAsync(threshold));

        // Arm 4 — the override's weekly norm.
        var position = await SeedPositionAsync();
        var (overrideId, v1) = await CreateOverrideAsync(
            admin, OverrideBody(code, Ok26, position, maxFlexBalance: 100m, weeklyNormHours: 37m, appliesFrom: "2026-11-02"));

        using (var refusedEdit = await EditOverrideAsync(
                   admin, overrideId, v1, OverrideBody(code, Ok26, position, 100m, 32m, "2026-11-11")))
            await AssertRefusalAsync(refusedEdit, "WeeklyNormHours", "NOT_MONDAY_ALIGNED", nearestMondays);

        using (var refusedOff = await TransitionOverrideAsync(admin, overrideId, v1, "deactivate", "2026-11-11"))
            await AssertRefusalAsync(refusedOff, "WeeklyNormHours", "NOT_MONDAY_ALIGNED", nearestMondays);

        using var flexOnly = await EditOverrideAsync(
            admin, overrideId, v1, OverrideBody(code, Ok26, position, 150m, 37m, "2026-11-11"));
        Assert.True(flexOnly.StatusCode == HttpStatusCode.OK,
            $"a MaxFlexBalance-only edit mid-week must be accepted, got {(int)flexOnly.StatusCode}: {await flexOnly.Content.ReadAsStringAsync()}");

        // S148 5a c1 ruling 1.4 (Fable W1), arm 5: a CREATE that sets a weekly norm, dated mid-week, is refused
        // (a create changes the in-force norm: no value, then 37). Nothing is created.
        var createPosition = await SeedPositionAsync();
        using (var refusedCreate = await admin.PostAsJsonAsync(
                   "/api/admin/position-overrides",
                   OverrideBody(code, Ok26, createPosition, 100m, 37m, "2026-11-11")))
            await AssertRefusalAsync(refusedCreate, "WeeklyNormHours", "NOT_MONDAY_ALIGNED", nearestMondays);
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM position_override_configs WHERE position_code = @p", ("p", createPosition)));

        // Arm 6: an ACTIVATE of a norm-setting override, dated mid-week, is refused. Created 37 h Monday 2
        // November, switched off Monday 9 November (a Monday, so accepted), switched on Wednesday 11 November.
        var activatePosition = await SeedPositionAsync();
        var (toActivate, activateV1) = await CreateOverrideAsync(
            admin, OverrideBody(code, Ok26, activatePosition, 100m, 37m, "2026-11-02"));
        long switchedOffVersion;
        using (var switchedOff = await TransitionOverrideAsync(admin, toActivate, activateV1, "deactivate", "2026-11-09"))
        {
            Assert.Equal(HttpStatusCode.OK, switchedOff.StatusCode);
            switchedOffVersion = EtagOf(switchedOff);
        }
        using (var refusedOn = await TransitionOverrideAsync(admin, toActivate, switchedOffVersion, "activate", "2026-11-11"))
            await AssertRefusalAsync(refusedOn, "WeeklyNormHours", "NOT_MONDAY_ALIGNED", nearestMondays);

        // Arm 7 (B3 exemption): a config's FIRST version with a weekly norm, dated a Wednesday, is accepted:
        // no earlier version means no change of an in-force value.
        var firstCode = UniqueCode("K_FIRST_");
        var (first, firstVersion) = await CreateDraftAsync(admin, ConfigBody(firstCode));
        await PublishOkAsync(admin, first, firstVersion, "2026-11-11");
        Assert.Equal("ACTIVE", await StatusOfConfigAsync(first));
    }

    /// <summary>
    /// AC-C2e. Clock Monday 9 November 2026. A draft published dated Tuesday 10 November is refused, code
    /// <c>EFFECTIVE_FROM_NOT_TODAY_OR_PAST</c>, <c>nearestValid</c> today (<c>2026-11-09</c>), and stays a DRAFT;
    /// an override create dated Tuesday 10 November gets the same 400 and no override row exists for the key.
    ///
    /// <para>
    /// <b>Kind: F (CI)</b>, a Docker HTTP fact. <b>Red conditions</b> (argued against the base): the publish
    /// route ignores any date and answers 200, and the override create answers 201, so the 400 assertions fail.
    /// </para>
    /// </summary>
    [Fact]
    public async Task FutureDate_RefusedWithNearestValidToday_ConfigAndOverride()
    {
        using var admin = AdminAt(Monday9Nov2026);
        var code = UniqueCode("K_FUT_");

        var (draftId, draftVersion) = await CreateDraftAsync(admin, ConfigBody(code));
        using (var refused = await PublishAsync(admin, draftId, draftVersion, "2026-11-10"))
            await AssertRefusalAsync(refused, "AppliesFrom", "EFFECTIVE_FROM_NOT_TODAY_OR_PAST", "2026-11-09");
        Assert.Equal("DRAFT", await StatusOfConfigAsync(draftId));

        var position = await SeedPositionAsync();
        using (var refused = await admin.PostAsJsonAsync(
                   "/api/admin/position-overrides", OverrideBody(code, Ok26, position, 100m, null, "2026-11-10")))
            await AssertRefusalAsync(refused, "AppliesFrom", "EFFECTIVE_FROM_NOT_TODAY_OR_PAST", "2026-11-09");
        Assert.Equal(0L, await CountAsync(
            "SELECT COUNT(*) FROM position_override_configs WHERE agreement_code = @code", ("code", code)));
    }

    /// <summary>
    /// AC-C2q (the predecessor rule seen refusing). Clock Monday 16 November 2026. Config arm: v1 published
    /// dated Monday 9 November; a draft published dated Monday 2 November is refused, code
    /// <c>BEFORE_PREDECESSOR</c>, <c>nearestValid</c> <c>["2026-11-09"]</c>, and stays a DRAFT with no PUBLISHED
    /// audit row; the same draft dated 9 November is accepted (an equal date is allowed). Override arm: an
    /// override written dated 9 November; an edit dated 2 November gets the same 400 and changes nothing (the
    /// head version and the version-row count are as before); the same edit dated 9 November is accepted.
    ///
    /// <para>
    /// <b>Kind: F (CI)</b>. <b>Red conditions</b> (argued against the base): the routes ignore the date and answer
    /// 200 for the 2 November publish and edit, so the 400 assertions fail. The <c>field</c> asserted is
    /// <c>AppliesFrom</c>, the name TASK-14818 built (its report § 3; declared to review, the validator's own
    /// name for the member is <c>EffectiveFrom</c>).
    /// </para>
    /// </summary>
    [Fact]
    public async Task DateBeforePredecessor_Refused_ConfigAndOverride()
    {
        using var admin = AdminAt(Monday16Nov2026);
        var code = UniqueCode("K_PRED_");

        // Config arm.
        await CreateAndPublishAsync(admin, ConfigBody(code), "2026-11-09");
        var (draftId, draftVersion) = await CreateDraftAsync(admin, ConfigBody(code, maxFlexBalance: 120m));
        using (var refused = await PublishAsync(admin, draftId, draftVersion, "2026-11-02"))
            await AssertRefusalAsync(refused, "AppliesFrom", "BEFORE_PREDECESSOR", "2026-11-09");
        Assert.Equal("DRAFT", await StatusOfConfigAsync(draftId));
        Assert.Equal(0L, await PublishedAuditRowsAsync(draftId));
        await PublishOkAsync(admin, draftId, draftVersion, "2026-11-09");
        Assert.Equal("ACTIVE", await StatusOfConfigAsync(draftId));

        // Override arm.
        var position = await SeedPositionAsync();
        var (overrideId, v1) = await CreateOverrideAsync(
            admin, OverrideBody(code, Ok26, position, 100m, null, "2026-11-09"));
        using (var refused = await EditOverrideAsync(
                   admin, overrideId, v1, OverrideBody(code, Ok26, position, 110m, null, "2026-11-02")))
            await AssertRefusalAsync(refused, "AppliesFrom", "BEFORE_PREDECESSOR", "2026-11-09");
        Assert.Equal(1L, await ScalarAsync<long>(
            "SELECT version FROM position_override_configs WHERE override_id = @id", ("id", overrideId)));
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM position_override_config_versions WHERE override_id = @id", ("id", overrideId)));
        using var accepted = await EditOverrideAsync(
            admin, overrideId, v1, OverrideBody(code, Ok26, position, 110m, null, "2026-11-09"));
        Assert.True(accepted.StatusCode == HttpStatusCode.OK,
            $"an edit dated the predecessor's own day must be accepted, got {(int)accepted.StatusCode}");

        // S148 5a c1 ruling 1.4 (Fable W1): the predecessor rule holds ACROSS override ids of one key. A is
        // created dated 2 November and switched off dated 9 November; a new override B (a new id) created for
        // the same key dated 2 November is refused against A's newest version.
        var crossPosition = await SeedPositionAsync();
        var (a, aV1) = await CreateOverrideAsync(
            admin, OverrideBody(code, Ok26, crossPosition, 100m, null, "2026-11-02"));
        using (var off = await TransitionOverrideAsync(admin, a, aV1, "deactivate", "2026-11-09"))
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        using (var refusedB = await admin.PostAsJsonAsync(
                   "/api/admin/position-overrides", OverrideBody(code, Ok26, crossPosition, 100m, null, "2026-11-02")))
            await AssertRefusalAsync(refusedB, "AppliesFrom", "BEFORE_PREDECESSOR", "2026-11-09");
        Assert.Equal(1L, await CountAsync(
            "SELECT COUNT(*) FROM position_override_configs WHERE position_code = @p", ("p", crossPosition)));
    }

    /// <summary>
    /// AC-C2l (ADR-041's testing consequence): an omitted date applies from the Copenhagen today. The clock is
    /// pinned at Sunday 11 January 2026 23:30 UTC, which is Monday 12 January 2026, 00:30 in Copenhagen: the UTC
    /// day and the Copenhagen day disagree, and the literal expected date is <c>2026-01-12</c> (a Monday, so
    /// admissible whatever the Monday list holds). K / OK24: v1 published dated Monday 5 January 2026; v2 (the
    /// same weekly norm, another flex cap) published with no body: its date is 2026-01-12 and its PUBLISHED
    /// audit row's <c>new_data</c> holds it. An override with no weekly norm created with no date: head and
    /// version row dated 2026-01-12, and the CREATED audit row holds it.
    ///
    /// <para>
    /// <b>Kind: F (CI)</b>. <b>Red conditions</b> (argued against the base): the base's PUBLISHED row carries no
    /// date and the override's head and version rows carry no date, so the date assertions fail; a UTC
    /// implementation would date both rows 2026-01-11 and fail the literal.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OmittedDate_AppliesFromCopenhagenToday_Audited()
    {
        var instant = new DateTimeOffset(2026, 1, 11, 23, 30, 0, TimeSpan.Zero);
        using var admin = AdminAtInstant(instant);
        var code = UniqueCode("K_OMIT_");

        await CreateAndPublishAsync(admin, ConfigBody(code, "OK24"), "2026-01-05");
        var (v2, v2Version) = await CreateDraftAsync(admin, ConfigBody(code, "OK24", maxFlexBalance: 80m));
        await PublishOkAsync(admin, v2, v2Version); // no body: the date is omitted

        Assert.Equal("2026-01-12", await ScalarAsync<string>(
            "SELECT to_char(applies_from, 'YYYY-MM-DD') FROM agreement_configs WHERE config_id = @id", ("id", v2)));
        Assert.Equal("2026-01-12", await ScalarAsync<string>(
            "SELECT new_data ->> 'appliesFrom' FROM agreement_config_audit WHERE config_id = @id AND action = 'PUBLISHED'",
            ("id", v2)));

        var position = await SeedPositionAsync();
        var (overrideId, _) = await CreateOverrideAsync(
            admin, OverrideBody(code, "OK24", position, maxFlexBalance: 100m, weeklyNormHours: null, appliesFrom: null));
        Assert.Equal("2026-01-12", await ScalarAsync<string>(
            "SELECT to_char(applies_from, 'YYYY-MM-DD') FROM position_override_configs WHERE override_id = @id",
            ("id", overrideId)));
        Assert.Equal("2026-01-12", await ScalarAsync<string>(
            "SELECT to_char(applies_from, 'YYYY-MM-DD') FROM position_override_config_versions WHERE override_id = @id",
            ("id", overrideId)));
        Assert.Equal("2026-01-12", await ScalarAsync<string>(
            "SELECT new_data ->> 'AppliesFrom' FROM position_override_config_audit WHERE override_id = @id AND action = 'CREATED'",
            ("id", overrideId)));
    }
}
