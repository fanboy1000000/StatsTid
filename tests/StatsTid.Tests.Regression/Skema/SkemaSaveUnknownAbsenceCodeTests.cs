using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S147 / TASK-14708-T (QUAL-231, AC-231-3, NR-4) — the Skema save refuses an absence code that is not in the
/// absence vocabulary.
///
/// <para>
/// <b>The story, in plain language.</b> The save stores whatever absence code the request carries, payroll then passes
/// it on, and a code the rule engine does not know counts as neither credit nor reduction. That silent drift is exactly
/// what QUAL-231 was (a stored code the engine's list did not spell). So a code outside the vocabulary is now refused
/// with <b>422</b> and a Danish message naming the CODE, before anything is looked up, locked or written. The body names
/// no date (ADR-040 D7: the gate is about the code, not the day).
/// </para>
///
/// <para>
/// <b>Expected values come from the spec</b> (the owner-ruled contract in TASK-14708): status 422; the body is exactly
/// <c>{ "error": "absence_type_unknown", "absenceType": "&lt;code&gt;", "message": "Fraværstypen '&lt;code&gt;' findes ikke." }</c>.
/// The refused codes are the OLD numbered child spellings (what the engine used to list but nothing ever stored), a
/// lower-case spelling (stored codes are case-exact), a made-up code and the empty string.
/// </para>
///
/// <para>Docker-gated (Testcontainers Postgres + full <c>init.sql</c>): <b>CI-pending</b>, never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaSaveUnknownAbsenceCodeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY_S147_UNKNOWNCODE";

    // Wednesday 10 June 2026 and Thursday 11 June 2026: weekdays (S138).
    private static readonly DateOnly Day1 = new(2026, 6, 10);
    private static readonly DateOnly Day2 = new(2026, 6, 11);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot first: the start-up seeders run at boot; every employee below is created after it.
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// AC-231-3 (kind F, Docker). A save carrying ONE absence with a code outside the vocabulary answers
    /// <b>422</b> with exactly the three-member body above, names no date, and writes nothing: no
    /// <c>absences_projection</c> row for the employee.
    ///
    /// <para><b>Red conditions (F; Docker).</b> Before S147 the save had no vocabulary gate: an unknown code is no
    /// entitlement type, so it was saved like a sick day, answering <b>200</b> and persisting a row, failing
    /// <c>status == 422</c> (and <c>rows == 0</c>).</para>
    /// </summary>
    [Theory]
    [InlineData("CHILD_SICK_1")]
    [InlineData("CHILD_SICK_2")]
    [InlineData("CHILD_SICK_3")]
    [InlineData("sick_day")]
    [InlineData("NOT_A_CODE")]
    [InlineData("")]
    public async Task UnknownAbsenceCode_IsRefused422_NamesTheCode_NoDate_NothingWritten(string unknownCode)
    {
        var employeeId = await NewEmployeeAsync();

        var (status, json) = await SaveAsync(employeeId, (Day1, unknownCode, 7.4m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        using var doc = JsonDocument.Parse(json);
        var body = doc.RootElement;
        var members = body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Multiple(
            () => Assert.Equal(new[] { "absenceType", "error", "message" }, members),
            () => Assert.Equal("absence_type_unknown", body.GetProperty("error").GetString()),
            () => Assert.Equal(unknownCode, body.GetProperty("absenceType").GetString()),
            () => Assert.Equal($"Fraværstypen '{unknownCode}' findes ikke.", body.GetProperty("message").GetString()),
            // ADR-040 D7: no date of any kind in the body, in either format the endpoint uses.
            () => Assert.DoesNotContain("2026-06-10", json),
            () => Assert.DoesNotContain("10-06-2026", json));
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId));
    }

    /// <summary>
    /// AC-231-3, the WHOLE save is refused (kind F, Docker). A save with a valid <c>SICK_DAY</c> on one day and an
    /// unknown code on the next answers 422 and writes NEITHER row: one bad cell fails the whole save.
    ///
    /// <para><b>Red conditions (F; Docker).</b> Before S147 both rows were accepted: status 200 and two rows.</para>
    /// </summary>
    [Fact]
    public async Task UnknownAbsenceCode_AmongValidOnes_RefusesTheWholeSave()
    {
        var employeeId = await NewEmployeeAsync();

        var (status, json) = await SaveAsync(employeeId, (Day1, "SICK_DAY", 7.4m), (Day2, "NOT_A_CODE", 7.4m));

        Assert.True(status == HttpStatusCode.UnprocessableEntity, $"expected 422, got {(int)status}: {json}");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("NOT_A_CODE", doc.RootElement.GetProperty("absenceType").GetString());
        Assert.Equal(0, await CountAbsenceRowsAsync(employeeId));
    }

    /// <summary>
    /// The control (kind B, "boundary pin — holds today"). The same save with only the valid <c>SICK_DAY</c> answers 200
    /// and persists exactly one row, so the gate is not a blanket refusal of absences and the 422 facts above cannot
    /// pass by a save that always fails.
    /// </summary>
    [Fact]
    public async Task KnownAbsenceCode_SickDay_IsAccepted_Control()
    {
        var employeeId = await NewEmployeeAsync();

        var (status, json) = await SaveAsync(employeeId, (Day1, "SICK_DAY", 7.4m));

        Assert.True(status == HttpStatusCode.OK, $"expected 200, got {(int)status}: {json}");
        Assert.Equal(1, await CountAbsenceRowsAsync(employeeId));
    }

    // ── helpers ──

    private async Task<string> NewEmployeeAsync()
    {
        var employeeId = "EMP-S147-UC-" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgId, "HK", "OK26", partTimeFraction: 1.0m);
        return employeeId;
    }

    private async Task<(HttpStatusCode Status, string Json)> SaveAsync(
        string employeeId, params (DateOnly Date, string Type, decimal Hours)[] absences)
    {
        var body = new
        {
            year = 2026,
            month = 6,
            absences = absences.Select(a => new
            {
                date = a.Date.ToString("yyyy-MM-dd"),
                absenceType = a.Type,
                hours = a.Hours,
            }).ToArray(),
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/skema/{employeeId}/save")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintEmployeeToken(employeeId));
        var response = await _client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<int> CountAbsenceRowsAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM absences_projection WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static string MintEmployeeToken(string actorId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.Employee,
            agreementCode: "HK",
            orgId: OrgId,
            scopes: new[] { new RoleScope(StatsTidRoles.Employee, OrgId, "ORG_ONLY") });
    }
}
