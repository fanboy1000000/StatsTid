using System.Net;
using System.Text;
using System.Text.Json;
using static StatsTid.Tests.Regression.Organisation.TransferWriteTests;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S148 / TASK-14832-T (AC-C4b, AC-C4f, AC-C4g route halves; ADR-043 D2, D3, D6; SEC-061) - the local profile's
/// save refuses a bad value with a 400 that names the field and a code, and stores nothing. The pure half has the
/// same three names in <c>StatsTid.Tests.Unit.Config.LocalProfileValidationTests</c>.
///
/// <para>
/// <b>In plain language.</b> A LocalAdmin who types a negative cap, a weekday window that ends before it starts, a
/// weekly norm of 0, a misspelled option or a number the database could not hold gets a 400 naming the field. Nothing
/// is half-written: no profile row, no audit row, no event. The values at the edge of what is allowed (0 for the two
/// maxima, "not set" everywhere) are accepted.
/// </para>
///
/// <para>
/// <b>Method.</b> Clock Thursday 5 March 2026, actor <c>ADMIN_S148_ORG</c>, organisation STY01, OK24; each case is a
/// first create (<c>If-None-Match: *</c>) on its own agreement code, dated Monday 2 March 2026. Codes, field names and
/// the <c>error</c> text are literals. "Nothing stored" is: no <c>local_agreement_profiles</c> row for the key, the
/// count of <c>local_agreement_profile_audit</c> rows unchanged across the request (a refused create has no profile id
/// to filter by, and the container is shared), and no <c>outbox_events</c> row on the key's stream. Docker-gated:
/// <b>CI-pending</b>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LocalProfileValidationTests : IClassFixture<LocalProfileHostFixture>
{
    private readonly LocalProfileHostFixture _fx;

    public LocalProfileValidationTests(LocalProfileHostFixture fx) => _fx = fx;

    /// <summary>
    /// <b>F (CI).</b> AC-C4b's route half. (1) <c>flexPlusCapHours: -1</c> is refused with
    /// <c>FlexPlusCapHours / MUST_NOT_BE_NEGATIVE</c>; (2) a Monday window 16:00 to 08:00 with
    /// <c>OfficeHoursMonEnd / WINDOW_START_NOT_BEFORE_END</c>; (3) <c>paidAbsenceCreditBasis: "OFFICE_HOURS"</c> with
    /// no window with <c>PaidAbsenceCreditBasis / OFFICE_HOURS_WITHOUT_WINDOWS</c>. Each: 400, <c>error</c>
    /// "Profile validation failed", nothing stored.
    /// <para><b>Product code between the readings:</b> the PUT.</para>
    /// <para><b>Red conditions (F).</b> Before TASK-14832 the route ignores the three members, answers 200 and stores
    /// a profile, so the 400 assertion fails first.</para>
    /// </summary>
    [Theory]
    [InlineData("C4B1", """{"effectiveFrom":"2026-03-02","flexPlusCapHours":-1}""", "FlexPlusCapHours", "MUST_NOT_BE_NEGATIVE")]
    [InlineData("C4B2", """{"effectiveFrom":"2026-03-02","officeHoursMonStart":"16:00:00","officeHoursMonEnd":"08:00:00"}""", "OfficeHoursMonEnd", "WINDOW_START_NOT_BEFORE_END")]
    [InlineData("C4B3", """{"effectiveFrom":"2026-03-02","paidAbsenceCreditBasis":"OFFICE_HOURS"}""", "PaidAbsenceCreditBasis", "OFFICE_HOURS_WITHOUT_WINDOWS")]
    public async Task NegativeCap_InvertedWindow_OfficeHoursWithoutWindows_400(string code, string json, string field, string errorCode) =>
        await AssertRefusedAsync(code, json, field, errorCode);

    /// <summary>
    /// <b>F (CI).</b> AC-C4f's route half (SEC-061). Refused, 400, nothing stored: <c>weeklyNormHours</c> 0 and -5
    /// (<c>MUST_BE_POSITIVE</c>), <c>maxFlexBalance</c> -0.01 and <c>maxOvertimeHoursPerPeriod</c> -1
    /// (<c>MUST_NOT_BE_NEGATIVE</c>). Accepted, 200, stored: <c>maxFlexBalance</c> 0 and
    /// <c>maxOvertimeHoursPerPeriod</c> 0 (the row holds 0), and every member null (a body of <c>effectiveFrom</c>
    /// only; the row's 22 settable columns all NULL).
    /// <para><b>Product code between the readings:</b> the PUT.</para>
    /// <para><b>Red conditions (F).</b> Today <c>weeklyNormHours: 0</c> answers 200 and is stored, so its 400
    /// assertion fails; the accepted cases pass today and stay as the discriminating half.</para>
    /// </summary>
    [Theory]
    [InlineData("C4F1", """{"effectiveFrom":"2026-03-02","weeklyNormHours":0}""", "WeeklyNormHours", "MUST_BE_POSITIVE")]
    [InlineData("C4F2", """{"effectiveFrom":"2026-03-02","weeklyNormHours":-5}""", "WeeklyNormHours", "MUST_BE_POSITIVE")]
    [InlineData("C4F3", """{"effectiveFrom":"2026-03-02","maxFlexBalance":-0.01}""", "MaxFlexBalance", "MUST_NOT_BE_NEGATIVE")]
    [InlineData("C4F4", """{"effectiveFrom":"2026-03-02","maxOvertimeHoursPerPeriod":-1}""", "MaxOvertimeHoursPerPeriod", "MUST_NOT_BE_NEGATIVE")]
    [InlineData("C4F5", """{"effectiveFrom":"2026-03-02","maxFlexBalance":0}""", null, "max_flex_balance")]
    [InlineData("C4F6", """{"effectiveFrom":"2026-03-02","maxOvertimeHoursPerPeriod":0}""", null, "max_overtime_hours_per_period")]
    [InlineData("C4F7", """{"effectiveFrom":"2026-03-02"}""", null, null)]
    public async Task LegacyNumericField_OutOfRange_400(string code, string json, string? field, string? errorCodeOrColumn)
    {
        if (field is not null)
        {
            await AssertRefusedAsync(code, json, field, errorCodeOrColumn!);
            return;
        }

        using var rsp = await PutCreateAsync(code, json);
        var text = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.OK, $"{code}: expected 200, got {(int)rsp.StatusCode}: {text}");
        Assert.Equal(1L, await ScalarLongAsync(_fx.Cs, ProfileRowsSql, ("c", code)));
        if (errorCodeOrColumn is not null)
        {
            // The accepted zero is stored as 0, not dropped to NULL ("not set").
#pragma warning disable CA2100 // the column is one of two literals in this fact's own InlineData; the key flows through a parameter (QUAL-073).
            var zero = await ScalarLongAsync(_fx.Cs,
                $"SELECT COUNT(*) FROM local_agreement_profiles WHERE org_id = 'STY01' AND agreement_code = @c AND ok_version = 'OK24' AND {errorCodeOrColumn} = 0",
                ("c", code));
#pragma warning restore CA2100
            Assert.Equal(1L, zero);
        }
        else
        {
            // Every member null: all 22 settable columns NULL.
            Assert.Equal(1L, await ScalarLongAsync(_fx.Cs, AllSettableNullSql, ("c", code)));
        }
    }

    /// <summary>
    /// <b>F (CI).</b> AC-C4g's route half (ruling 15; the Unit theory has the same name). Refused, 400, nothing
    /// stored: <c>flexSettlementPeriod: "month"</c> (<c>UNKNOWN_VALUE</c>), a lone start
    /// <c>officeHoursMonStart: "08:00:00"</c> with no end (<c>OfficeHoursMonEnd / WINDOW_INCOMPLETE</c>),
    /// <c>flexPlusCapHours: 10000</c> (<c>EXCEEDS_STORAGE_LIMIT</c>, past <c>NUMERIC(6,2)</c>).
    /// <para><b>Product code between the readings:</b> the PUT.</para>
    /// <para><b>Red conditions (F).</b> Before TASK-14832 each answers 200 (the route ignores the three members and
    /// stores a profile), so the 400 assertion fails first.</para>
    /// </summary>
    [Theory]
    [InlineData("C4G1", """{"effectiveFrom":"2026-03-02","flexSettlementPeriod":"month"}""", "FlexSettlementPeriod", "UNKNOWN_VALUE")]
    [InlineData("C4G2", """{"effectiveFrom":"2026-03-02","officeHoursMonStart":"08:00:00"}""", "OfficeHoursMonEnd", "WINDOW_INCOMPLETE")]
    [InlineData("C4G3", """{"effectiveFrom":"2026-03-02","flexPlusCapHours":10000}""", "FlexPlusCapHours", "EXCEEDS_STORAGE_LIMIT")]
    public async Task ValueTheTableCannotHold_400(string code, string json, string field, string errorCode) =>
        await AssertRefusedAsync(code, json, field, errorCode);

    // ─── helpers ───

    private const string ProfileRowsSql =
        "SELECT COUNT(*) FROM local_agreement_profiles WHERE org_id = 'STY01' AND agreement_code = @c AND ok_version = 'OK24'";

    private const string AllSettableNullSql =
        """
        SELECT COUNT(*) FROM local_agreement_profiles
        WHERE org_id = 'STY01' AND agreement_code = @c AND ok_version = 'OK24'
          AND weekly_norm_hours IS NULL AND max_flex_balance IS NULL AND max_overtime_hours_per_period IS NULL
          AND overtime_requires_pre_approval IS NULL
          AND flex_settlement_period IS NULL AND flex_cap_test_period IS NULL
          AND flex_plus_cap_hours IS NULL AND flex_minus_cap_hours IS NULL AND flex_daily_credit_cap_hours IS NULL
          AND flex_above_cap_action IS NULL AND flex_leaving_action IS NULL
          AND office_hours_mon_start IS NULL AND office_hours_mon_end IS NULL
          AND office_hours_tue_start IS NULL AND office_hours_tue_end IS NULL
          AND office_hours_wed_start IS NULL AND office_hours_wed_end IS NULL
          AND office_hours_thu_start IS NULL AND office_hours_thu_end IS NULL
          AND office_hours_fri_start IS NULL AND office_hours_fri_end IS NULL
          AND paid_absence_credit_basis IS NULL
        """;

    private async Task<HttpResponseMessage> PutCreateAsync(string code, string json)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/config/STY01/profile/{code}/OK24")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return await _fx.Client().SendAsync(req);
    }

    private Task<long> AuditCountAsync() =>
        ScalarLongAsync(_fx.Cs, "SELECT COUNT(*) FROM local_agreement_profile_audit");

    /// <summary>The first create answers 400 <c>Profile validation failed</c> with the one (field, code) pair in
    /// <c>fields</c>, and nothing is stored.</summary>
    private async Task AssertRefusedAsync(string code, string json, string field, string errorCode)
    {
        var auditBefore = await AuditCountAsync();

        using var rsp = await PutCreateAsync(code, json);
        var text = await rsp.Content.ReadAsStringAsync();

        Assert.True(rsp.StatusCode == HttpStatusCode.BadRequest, $"{code}: expected 400, got {(int)rsp.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("Profile validation failed", doc.RootElement.GetProperty("error").GetString());
        var pairs = doc.RootElement.GetProperty("fields").EnumerateArray()
            .Select(f => (Field: f.GetProperty("field").GetString(), Code: f.GetProperty("code").GetString()))
            .ToList();
        Assert.Contains((field, errorCode), pairs);

        Assert.Equal(0L, await ScalarLongAsync(_fx.Cs, ProfileRowsSql, ("c", code)));
        Assert.Equal(auditBefore, await AuditCountAsync());
        Assert.Equal(0L, await ScalarLongAsync(_fx.Cs,
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s", ("s", $"local-agreement-profile-STY01-{code}-OK24")));
    }
}
