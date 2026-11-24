using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// A Backend host with the clock fixed at Thursday 5 March 2026 over one container. Each of the two profile
/// test classes of S148 / TASK-14832-T has its own fixture (<c>IClassFixture</c>), so its own container; within a
/// class every fact uses its own agreement code. The class is serial with every other class
/// (<c>xunit.runner.json</c>: one collection at a time).
/// </summary>
public sealed class LocalProfileHostFixture : IAsyncLifetime
{
    public static readonly DateOnly Today = new(2026, 3, 5);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    public string Cs => _harness.ConnectionString;

    /// <summary>A GlobalAdmin (<c>ADMIN_S148_ORG</c>), which passes the route's LocalAdmin-floor scope check.</summary>
    public HttpClient Client() => GlobalAdminClient(_host);

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(Cs);
        _factory = new StatsTidWebApplicationFactory(Cs, withoutLeaverJob: true);
        _host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Today))));
        _ = _host.CreateClient(); // boot the seeders before any fact seeds
    }

    public async Task DisposeAsync()
    {
        _host?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}

/// <summary>
/// S148 / TASK-14832-T (AC-C4a, AC-C4e; ADR-043 D6; ADR-017 D6, D9) - a LocalAdmin's eighteen new settings on the
/// institution's local profile (flex periods and caps, the two flex actions, office hours per weekday, the credit
/// basis for paid absence) are stored, read back and audited, and a save that changes any one of them is never
/// mistaken for "nothing changed".
///
/// <para>
/// <b>In plain language.</b> Saving a profile twice on the same day edits it in place and bumps its version, unless
/// nothing changed, when the save is skipped on purpose (no version, no audit row, no event). Before TASK-14832 the
/// "did anything change?" test looked at four settings only, so a save that changed just the plus cap was thrown
/// away silently. These facts pin that every one of the eighteen counts, that clearing a set one counts, and that a
/// truly unchanged save is still skipped.
/// </para>
///
/// <para>
/// <b>Method.</b> Clock Thursday 5 March 2026, actor <c>ADMIN_S148_ORG</c>, organisation STY01, OK24, every write
/// dated Monday 2 March 2026 (a Monday, so a weekly-norm value is never refused for alignment). Each fact has its own
/// agreement code, so every count is filtered to that key or its stream. Expected values are literals (a wrong
/// production constant must fail). Numbers in a delta are compared by value, not text: a value read back from a
/// <c>NUMERIC(6,2)</c> column serializes as <c>37.00</c>. Docker-gated: <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LocalProfileNewColumnsTests : IClassFixture<LocalProfileHostFixture>
{
    private const string Org = "STY01";
    private const string Ok = "OK24";
    private const string EffectiveFrom = "2026-03-02";

    private readonly LocalProfileHostFixture _fx;

    public LocalProfileNewColumnsTests(LocalProfileHostFixture fx) => _fx = fx;

    /// <summary>The 22 settable members in the product's order: (model member, column).</summary>
    private static readonly (string Member, string Column)[] Settable =
    {
        ("WeeklyNormHours", "weekly_norm_hours"),
        ("MaxFlexBalance", "max_flex_balance"),
        ("MaxOvertimeHoursPerPeriod", "max_overtime_hours_per_period"),
        ("OvertimeRequiresPreApproval", "overtime_requires_pre_approval"),
        ("FlexSettlementPeriod", "flex_settlement_period"),
        ("FlexCapTestPeriod", "flex_cap_test_period"),
        ("FlexPlusCapHours", "flex_plus_cap_hours"),
        ("FlexMinusCapHours", "flex_minus_cap_hours"),
        ("FlexDailyCreditCapHours", "flex_daily_credit_cap_hours"),
        ("FlexAboveCapAction", "flex_above_cap_action"),
        ("FlexLeavingAction", "flex_leaving_action"),
        ("OfficeHoursMonStart", "office_hours_mon_start"),
        ("OfficeHoursMonEnd", "office_hours_mon_end"),
        ("OfficeHoursTueStart", "office_hours_tue_start"),
        ("OfficeHoursTueEnd", "office_hours_tue_end"),
        ("OfficeHoursWedStart", "office_hours_wed_start"),
        ("OfficeHoursWedEnd", "office_hours_wed_end"),
        ("OfficeHoursThuStart", "office_hours_thu_start"),
        ("OfficeHoursThuEnd", "office_hours_thu_end"),
        ("OfficeHoursFriStart", "office_hours_fri_start"),
        ("OfficeHoursFriEnd", "office_hours_fri_end"),
        ("PaidAbsenceCreditBasis", "paid_absence_credit_basis"),
    };

    /// <summary>The eighteen new settings with the values AC-C4a sends (a 36-hour week: the server never refuses
    /// a sum that misses 37 hours). A number is a decimal, a clock time a <c>"HH:mm:ss"</c> string.</summary>
    private static readonly (string Member, object Value)[] Eighteen =
    {
        ("FlexSettlementPeriod", "MONTH"),
        ("FlexCapTestPeriod", "MONTH"),
        ("FlexPlusCapHours", 37m),
        ("FlexMinusCapHours", 10m),
        ("FlexDailyCreditCapHours", 13m),
        ("FlexAboveCapAction", "FORFEIT"),
        ("FlexLeavingAction", "FORFEIT_POSITIVE_RECORD_NEGATIVE"),
        ("OfficeHoursMonStart", "08:00:00"), ("OfficeHoursMonEnd", "15:30:00"),
        ("OfficeHoursTueStart", "08:00:00"), ("OfficeHoursTueEnd", "15:30:00"),
        ("OfficeHoursWedStart", "08:00:00"), ("OfficeHoursWedEnd", "15:30:00"),
        ("OfficeHoursThuStart", "08:00:00"), ("OfficeHoursThuEnd", "15:30:00"),
        ("OfficeHoursFriStart", "08:00:00"), ("OfficeHoursFriEnd", "14:00:00"),
        ("PaidAbsenceCreditBasis", "OFFICE_HOURS"),
    };

    private static string Stream(string code) => $"local-agreement-profile-{Org}-{code}-{Ok}";

    // ─── AC-C4a ───

    /// <summary>
    /// <b>F (CI).</b> AC-C4a. One first create on key <c>C4A</c> with every legacy member null and the eighteen set
    /// is stored (read by SQL), read back (the PUT's 200 body and a GET), audited (one <c>CREATED</c> row whose
    /// <c>delta_jsonb</c> has exactly the eighteen keys, each <c>Old</c> null and <c>New</c> the value sent) and
    /// emitted (one outbox event on the key's stream with the same eighteen keys). The windows sum to 36 hours, so
    /// the 200 also pins that the server never refuses a week that does not sum to 37 hours.
    /// <para><b>Product code between the readings:</b> the PUT (validator, repository INSERT, audit, outbox) and
    /// the GET (<c>MapReader</c>).</para>
    /// <para><b>Red conditions (F).</b> Before TASK-14832 the route binds none of the eighteen, so the stored
    /// columns are null and assertion (2), the stored-value check, fails first.</para>
    /// </summary>
    [Fact]
    public async Task EachColumn_WrittenReadBack_InAuditDelta()
    {
        const string code = "C4A";
        var client = _fx.Client();
        var body = new Dictionary<string, object?> { ["effectiveFrom"] = EffectiveFrom };
        foreach (var (member, value) in Eighteen)
            body[CamelCase(member)] = value;

        // (1) 200
        using var put = await PutAsync(client, code, body, ifMatch: null);
        var putText = await put.Content.ReadAsStringAsync();
        Assert.True(put.StatusCode == HttpStatusCode.OK, $"first create: {(int)put.StatusCode} {putText}");

        // (2) written: the row's columns hold the values sent.
        var row = await ReadSettableAsync(code);
        foreach (var (member, value) in Eighteen)
            AssertStored(member, value, row);

        // (3) read back: the PUT's 200 body and a following GET each carry the eighteen.
        using var putJson = JsonDocument.Parse(putText);
        AssertBodyCarries(putJson.RootElement, "PUT 200");
        using var get = await client.GetAsync($"/api/config/{Org}/profile/{code}/{Ok}");
        var getText = await get.Content.ReadAsStringAsync();
        Assert.True(get.StatusCode == HttpStatusCode.OK, $"GET: {(int)get.StatusCode} {getText}");
        using var getJson = JsonDocument.Parse(getText);
        AssertBodyCarries(getJson.RootElement, "GET 200");

        // (4) in the audit delta: one CREATED row, exactly the eighteen keys, each Old null.
        var profileId = Guid.Parse(Prop(getJson.RootElement, "profileId").GetString()!);
        var audit = await AuditRowsAsync(profileId);
        var created = Assert.Single(audit);
        Assert.Equal("CREATED", created.Action);
        using var delta = JsonDocument.Parse(created.Delta);
        AssertExactKeys(Eighteen.Select(e => e.Member), delta.RootElement, "the CREATED audit delta");
        foreach (var (member, value) in Eighteen)
        {
            var change = delta.RootElement.GetProperty(member);
            AssertJsonValue(null, Prop(change, "Old"), $"{member} Old");
            AssertJsonValue(value, Prop(change, "New"), $"{member} New");
        }

        // (5) one outbox event on the key's stream with the same eighteen keys.
        var events = await OutboxPayloadsAsync(code);
        var evt = Assert.Single(events);
        using var evtJson = JsonDocument.Parse(evt);
        AssertExactKeys(Eighteen.Select(e => e.Member), Prop(evtJson.RootElement, "ChangedFields"), "the event's changed fields");
    }

    // ─── AC-C4e ───

    /// <summary>
    /// <b>F (CI), every case.</b> AC-C4e. The baseline of each case, seeded by SQL on its own key
    /// <c>C4E01</c> ... <c>C4E18</c>, is one open row from Monday 2 March 2026 at version 1 with the ten office-hours
    /// columns set (Monday to Thursday 08:00-16:00, Friday 08:00-13:00) and everything else null. The save sends the
    /// baseline's ten clock values plus ONE change (an office-hours case moves one bound of an existing window,
    /// because a lone start or end is refused). Expected: 200 at version 2 (the body, the ETag and the row), the
    /// same <c>profile_id</c>, exactly one new <c>MODIFIED</c> audit row whose delta names that column and only that
    /// column with <c>Old</c> the baseline and <c>New</c> the value sent, exactly one outbox event whose changed
    /// fields hold that one key, the column holding the value sent and the other twenty-one unchanged.
    /// <para><b>What each assertion catches:</b> a column left out of the no-op comparator leaves the version at 1;
    /// out of <c>ComputeChangedFields</c>, the delta lacks the key; out of the UPDATE, the stored value stays the
    /// baseline; out of <c>MapReader</c>, <c>Old</c> is null where the baseline is set (cases 8-17).</para>
    /// <para><b>Product code between the readings:</b> the PUT (pre-read through <c>MapReader</c>, comparator,
    /// UPDATE, audit, outbox).</para>
    /// <para><b>Red conditions (F).</b> Before TASK-14832 the route ignores the sent member and <c>MapReader</c>
    /// reads none of the eighteen, so the comparator sees four equal legacy members and the save is a no-op: the
    /// version stays 1 and the version assertion fails first.</para>
    /// </summary>
    [Theory]
    [InlineData(1, "FlexSettlementPeriod", null, "MONTH")]
    [InlineData(2, "FlexCapTestPeriod", null, "MONTH")]
    [InlineData(3, "FlexPlusCapHours", null, 37)]
    [InlineData(4, "FlexMinusCapHours", null, 10)]
    [InlineData(5, "FlexDailyCreditCapHours", null, 13)]
    [InlineData(6, "FlexAboveCapAction", null, "FORFEIT")]
    [InlineData(7, "FlexLeavingAction", null, "FORFEIT_POSITIVE_RECORD_NEGATIVE")]
    [InlineData(8, "OfficeHoursMonStart", "08:00:00", "08:30:00")]
    [InlineData(9, "OfficeHoursMonEnd", "16:00:00", "15:30:00")]
    [InlineData(10, "OfficeHoursTueStart", "08:00:00", "08:30:00")]
    [InlineData(11, "OfficeHoursTueEnd", "16:00:00", "15:30:00")]
    [InlineData(12, "OfficeHoursWedStart", "08:00:00", "08:30:00")]
    [InlineData(13, "OfficeHoursWedEnd", "16:00:00", "15:30:00")]
    [InlineData(14, "OfficeHoursThuStart", "08:00:00", "08:30:00")]
    [InlineData(15, "OfficeHoursThuEnd", "16:00:00", "15:30:00")]
    [InlineData(16, "OfficeHoursFriStart", "08:00:00", "08:30:00")]
    [InlineData(17, "OfficeHoursFriEnd", "13:00:00", "12:30:00")]
    [InlineData(18, "PaidAbsenceCreditBasis", null, "PARAGRAPH_7_NR_2")]
    public async Task SameDaySave_ChangingOnlyOneNewColumn_IsNotANoOp(int n, string member, string? baseline, object sent)
    {
        var code = $"C4E{n:00}";
        await SeedWindowsBaselineAsync(code);
        var profileId = await ProfileIdAsync(code);
        var before = await ReadSettableAsync(code);
        var auditBefore = (await AuditRowsAsync(profileId)).Count;
        var eventsBefore = (await OutboxPayloadsAsync(code)).Count;

        var body = WindowsBody();
        body[CamelCase(member)] = sent;
        using var put = await PutAsync(_fx.Client(), code, body, ifMatch: 1);
        var text = await put.Content.ReadAsStringAsync();

        Assert.True(put.StatusCode == HttpStatusCode.OK, $"{member}: {(int)put.StatusCode} {text}");
        using var json = JsonDocument.Parse(text);
        Assert.Equal(2L, Prop(json.RootElement, "version").GetInt64());
        Assert.Equal("\"2\"", put.Headers.ETag?.Tag);
        Assert.Equal(2L, await ScalarLongAsync(_fx.Cs,
            "SELECT version FROM local_agreement_profiles WHERE org_id = 'STY01' AND agreement_code = @c AND ok_version = 'OK24' AND effective_to IS NULL", ("c", code)));
        Assert.Equal(profileId, await ProfileIdAsync(code));

        var audit = await AuditRowsAsync(profileId);
        Assert.Equal(auditBefore + 1, audit.Count);
        var modified = audit[^1];
        Assert.Equal("MODIFIED", modified.Action);
        using var delta = JsonDocument.Parse(modified.Delta);
        AssertExactKeys(new[] { member }, delta.RootElement, $"{member}: the MODIFIED audit delta");
        var change = delta.RootElement.GetProperty(member);
        AssertJsonValue(baseline, Prop(change, "Old"), $"{member} Old");
        AssertJsonValue(sent, Prop(change, "New"), $"{member} New");

        var events = await OutboxPayloadsAsync(code);
        Assert.Equal(eventsBefore + 1, events.Count);
        using var evtJson = JsonDocument.Parse(events[^1]);
        AssertExactKeys(new[] { member }, Prop(evtJson.RootElement, "ChangedFields"), $"{member}: the event's changed fields");

        var after = await ReadSettableAsync(code);
        AssertStored(member, sent, after);
        foreach (var (other, _) in Settable.Where(s => s.Member != member))
            Assert.True(before[other] == after[other], $"{member}: {other} changed from '{before[other]}' to '{after[other]}'.");
    }

    /// <summary>
    /// <b>R.</b> AC-C4e's further fact. A profile with all eighteen new settings and <c>maxFlexBalance</c> 30 set
    /// (key <c>C4R</c>, version 1, seeded by SQL) saved again the same day with the same twenty-two values and
    /// <c>If-Match: "1"</c> is still a no-op: 200 at version 1 (body, ETag, row), no audit row, no outbox event,
    /// every column unchanged.
    /// <para><b>Product code between the readings:</b> the PUT (pre-read through <c>MapReader</c>, the
    /// twenty-two-member comparator, the no-op).</para>
    /// <para><b>Red conditions (R).</b> It passes on the code before TASK-14832 (neither side carries the eighteen)
    /// and must keep passing; it goes red if the comparator, <c>MapReader</c> or the binding treats an unchanged
    /// setting as changed (a clock time read back at a different precision, a string compared by reference).</para>
    /// </summary>
    [Fact]
    public async Task SameDaySave_NewSettingsSetAndUnchanged_IsStillNoOp()
    {
        const string code = "C4R";
        await ExecSqlAsync(
            """
            INSERT INTO local_agreement_profiles
                (profile_id, org_id, agreement_code, ok_version, effective_from, effective_to, max_flex_balance, created_by, version,
                 flex_settlement_period, flex_cap_test_period, flex_plus_cap_hours, flex_minus_cap_hours, flex_daily_credit_cap_hours,
                 flex_above_cap_action, flex_leaving_action,
                 office_hours_mon_start, office_hours_mon_end, office_hours_tue_start, office_hours_tue_end,
                 office_hours_wed_start, office_hours_wed_end, office_hours_thu_start, office_hours_thu_end,
                 office_hours_fri_start, office_hours_fri_end, paid_absence_credit_basis)
            VALUES (gen_random_uuid(), 'STY01', 'C4R', 'OK24', '2026-03-02', NULL, 30, 'seed_c4e', 1,
                    'MONTH', 'MONTH', 37, 10, 13, 'FORFEIT', 'FORFEIT_POSITIVE_RECORD_NEGATIVE',
                    '08:00', '15:30', '08:00', '15:30', '08:00', '15:30', '08:00', '15:30', '08:00', '14:00', 'OFFICE_HOURS')
            """);
        var profileId = await ProfileIdAsync(code);
        var before = await ReadSettableAsync(code);

        var body = new Dictionary<string, object?> { ["effectiveFrom"] = EffectiveFrom, ["maxFlexBalance"] = 30m };
        foreach (var (member, value) in Eighteen)
            body[CamelCase(member)] = value;
        using var put = await PutAsync(_fx.Client(), code, body, ifMatch: 1);
        var text = await put.Content.ReadAsStringAsync();

        Assert.True(put.StatusCode == HttpStatusCode.OK, $"{(int)put.StatusCode} {text}");
        using var json = JsonDocument.Parse(text);
        Assert.Equal(1L, Prop(json.RootElement, "version").GetInt64());
        Assert.Equal("\"1\"", put.Headers.ETag?.Tag);
        Assert.Equal(1L, await ScalarLongAsync(_fx.Cs,
            "SELECT version FROM local_agreement_profiles WHERE org_id = 'STY01' AND agreement_code = 'C4R' AND ok_version = 'OK24' AND effective_to IS NULL"));
        Assert.Empty(await AuditRowsAsync(profileId));
        Assert.Empty(await OutboxPayloadsAsync(code));
        Assert.Equal(before, await ReadSettableAsync(code));
    }

    /// <summary>
    /// <b>F (CI).</b> AC-C4e's clearing fact. A profile whose only set member is <c>flex_plus_cap_hours</c> 37
    /// (key <c>C4CLR</c>, version 1, seeded by SQL) saved with a body of <c>effectiveFrom</c> only: the save replaces
    /// the whole profile, so the omitted member is cleared, and a clear is a change, never a silent no-op.
    /// Expected: 200 at version 2 (body, ETag, row), the same <c>profile_id</c>, exactly one new <c>MODIFIED</c> row
    /// whose delta is <c>{"FlexPlusCapHours":{"Old":37,"New":null}}</c> (by value: <c>Old</c> reads back as
    /// <c>37.00</c>), exactly one outbox event with that one key, and the column NULL.
    /// <para><b>Product code between the readings:</b> the PUT (pre-read through <c>MapReader</c>, comparator,
    /// UPDATE, audit, outbox).</para>
    /// <para><b>Red conditions (F).</b> Before TASK-14832 <c>MapReader</c> reads none of the eighteen and the
    /// comparator compares four equal legacy members, so the save is a no-op: the version stays 1 and the version
    /// assertion fails first.</para>
    /// </summary>
    [Fact]
    public async Task SameDaySave_ClearingASetNewSetting_IsNotANoOp()
    {
        const string code = "C4CLR";
        await ExecSqlAsync(
            """
            INSERT INTO local_agreement_profiles
                (profile_id, org_id, agreement_code, ok_version, effective_from, effective_to, flex_plus_cap_hours, created_by, version)
            VALUES (gen_random_uuid(), 'STY01', 'C4CLR', 'OK24', '2026-03-02', NULL, 37, 'seed_c4e', 1)
            """);
        var profileId = await ProfileIdAsync(code);

        using var put = await PutAsync(_fx.Client(), code, new Dictionary<string, object?> { ["effectiveFrom"] = EffectiveFrom }, ifMatch: 1);
        var text = await put.Content.ReadAsStringAsync();

        Assert.True(put.StatusCode == HttpStatusCode.OK, $"{(int)put.StatusCode} {text}");
        using var json = JsonDocument.Parse(text);
        Assert.Equal(2L, Prop(json.RootElement, "version").GetInt64());
        Assert.Equal("\"2\"", put.Headers.ETag?.Tag);
        Assert.Equal(2L, await ScalarLongAsync(_fx.Cs,
            "SELECT version FROM local_agreement_profiles WHERE org_id = 'STY01' AND agreement_code = 'C4CLR' AND ok_version = 'OK24' AND effective_to IS NULL"));
        Assert.Equal(profileId, await ProfileIdAsync(code));

        var modified = Assert.Single(await AuditRowsAsync(profileId));
        Assert.Equal("MODIFIED", modified.Action);
        using var delta = JsonDocument.Parse(modified.Delta);
        AssertExactKeys(new[] { "FlexPlusCapHours" }, delta.RootElement, "the MODIFIED audit delta");
        var change = delta.RootElement.GetProperty("FlexPlusCapHours");
        AssertJsonValue(37m, Prop(change, "Old"), "FlexPlusCapHours Old");
        AssertJsonValue(null, Prop(change, "New"), "FlexPlusCapHours New");

        var evt = Assert.Single(await OutboxPayloadsAsync(code));
        using var evtJson = JsonDocument.Parse(evt);
        AssertExactKeys(new[] { "FlexPlusCapHours" }, Prop(evtJson.RootElement, "ChangedFields"), "the event's changed fields");
        Assert.Null((await ReadSettableAsync(code))["FlexPlusCapHours"]);
    }

    // ─── helpers ───

    private static string CamelCase(string member) => char.ToLowerInvariant(member[0]) + member[1..];

    /// <summary>The baseline's ten clock values (Monday-Thursday 08:00-16:00, Friday 08:00-13:00) as a request body.</summary>
    private static Dictionary<string, object?> WindowsBody() => new()
    {
        ["effectiveFrom"] = EffectiveFrom,
        ["officeHoursMonStart"] = "08:00:00", ["officeHoursMonEnd"] = "16:00:00",
        ["officeHoursTueStart"] = "08:00:00", ["officeHoursTueEnd"] = "16:00:00",
        ["officeHoursWedStart"] = "08:00:00", ["officeHoursWedEnd"] = "16:00:00",
        ["officeHoursThuStart"] = "08:00:00", ["officeHoursThuEnd"] = "16:00:00",
        ["officeHoursFriStart"] = "08:00:00", ["officeHoursFriEnd"] = "13:00:00",
    };

    private Task SeedWindowsBaselineAsync(string code) => ExecSqlAsync(
        """
        INSERT INTO local_agreement_profiles
            (profile_id, org_id, agreement_code, ok_version, effective_from, effective_to, created_by, version,
             office_hours_mon_start, office_hours_mon_end, office_hours_tue_start, office_hours_tue_end,
             office_hours_wed_start, office_hours_wed_end, office_hours_thu_start, office_hours_thu_end,
             office_hours_fri_start, office_hours_fri_end)
        VALUES (gen_random_uuid(), 'STY01', @c, 'OK24', '2026-03-02', NULL, 'seed_c4e', 1,
                '08:00', '16:00', '08:00', '16:00', '08:00', '16:00', '08:00', '16:00', '08:00', '13:00')
        """, ("c", code));

    private static async Task<HttpResponseMessage> PutAsync(
        HttpClient client, string code, Dictionary<string, object?> body, long? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/config/{Org}/profile/{code}/{Ok}")
        {
            Content = JsonContent.Create(body),
        };
        if (ifMatch is { } version)
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        else
            req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return await client.SendAsync(req);
    }

    /// <summary>A JSON member by name, ignoring case (the wire is camelCase, the audit delta PascalCase).</summary>
    private static JsonElement Prop(JsonElement element, string name)
    {
        foreach (var p in element.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        throw new Xunit.Sdk.XunitException($"No member '{name}' in {element}.");
    }

    private static void AssertExactKeys(IEnumerable<string> expected, JsonElement obj, string what)
    {
        var actual = obj.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.True(actual.SequenceEqual(expected.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal),
            $"{what}: expected keys [{string.Join(",", expected.OrderBy(x => x, StringComparer.Ordinal))}], got [{string.Join(",", actual)}].");
    }

    /// <summary>A JSON value equals the expected one: null, a string by text, a number by value (37 against 37.00).</summary>
    private static void AssertJsonValue(object? expected, JsonElement actual, string what)
    {
        switch (expected)
        {
            case null:
                Assert.True(actual.ValueKind == JsonValueKind.Null, $"{what}: expected null, got {actual}.");
                break;
            case string s:
                Assert.True(actual.ValueKind == JsonValueKind.String && actual.GetString() == s, $"{what}: expected \"{s}\", got {actual}.");
                break;
            default:
                var number = Convert.ToDecimal(expected, CultureInfo.InvariantCulture);
                Assert.True(actual.ValueKind == JsonValueKind.Number && actual.GetDecimal() == number, $"{what}: expected {number}, got {actual}.");
                break;
        }
    }

    private static void AssertBodyCarries(JsonElement body, string what)
    {
        foreach (var (member, value) in Eighteen)
            AssertJsonValue(value, Prop(body, member), $"{what}: {member}");
    }

    private static void AssertStored(string member, object value, IReadOnlyDictionary<string, string?> row)
    {
        var text = row[member];
        Assert.True(text is not null, $"{member}: the column is NULL; expected {value}.");
        if (value is string s)
            Assert.True(text == s, $"{member}: the column holds '{text}'; expected '{s}'.");
        else
        {
            var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            Assert.True(decimal.Parse(text!, CultureInfo.InvariantCulture) == number, $"{member}: the column holds '{text}'; expected {number}.");
        }
    }

    /// <summary>The 22 settable columns of the key's open row, each as text (NULL stays null), by model member name.</summary>
    private async Task<Dictionary<string, string?>> ReadSettableAsync(string code)
    {
        var select = string.Join(", ", Settable.Select(s => $"{s.Column}::text"));
#pragma warning disable CA2100 // the column list is a compile-time constant; the key flows through a parameter (QUAL-073).
        var sql = $"SELECT {select} FROM local_agreement_profiles WHERE org_id = 'STY01' AND agreement_code = @c AND ok_version = 'OK24' AND effective_to IS NULL";
        await using var conn = new NpgsqlConnection(_fx.Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        cmd.Parameters.AddWithValue("c", code);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"No open profile row for {code}.");
        var row = new Dictionary<string, string?>();
        for (var i = 0; i < Settable.Length; i++)
            row[Settable[i].Member] = reader.IsDBNull(i) ? null : reader.GetString(i);
        return row;
    }

    private Task<Guid> ProfileIdAsync(string code) => ScalarGuidAsync(
        "SELECT profile_id FROM local_agreement_profiles WHERE org_id = 'STY01' AND agreement_code = @c AND ok_version = 'OK24' AND effective_to IS NULL", ("c", code));

    private async Task<List<(string Action, string Delta)>> AuditRowsAsync(Guid profileId)
    {
        await using var conn = new NpgsqlConnection(_fx.Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT action, delta_jsonb::text FROM local_agreement_profile_audit WHERE profile_id = @p ORDER BY audit_id", conn);
        cmd.Parameters.AddWithValue("p", profileId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string, string)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.GetString(1)));
        return rows;
    }

    private async Task<List<string>> OutboxPayloadsAsync(string code)
    {
        await using var conn = new NpgsqlConnection(_fx.Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_payload::text FROM outbox_events WHERE stream_id = @s ORDER BY outbox_id", conn);
        cmd.Parameters.AddWithValue("s", Stream(code));
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }

#pragma warning disable CA2100 // each sql is a literal at its call site; values flow through parameters (QUAL-073).
    private async Task ExecSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(_fx.Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<Guid> ScalarGuidAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(_fx.Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }
#pragma warning restore CA2100
}
