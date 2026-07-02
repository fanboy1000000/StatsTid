using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace StatsTid.Tools.DemoSeed.Loading;

/// <summary>
/// S84 / TASK-8403 — a thin typed HTTP wrapper over the live Backend API. Holds the bearer
/// token after login; exposes the handful of endpoints the loader drives. camelCase JSON
/// (the API default); DateOnly is serialized as ISO yyyy-MM-dd by sending pre-formatted strings.
/// </summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private string? _token;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task LoginAsync(string username, string password, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("/api/auth/login",
            new { username, password }, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>(JsonOpts, ct)
                   ?? throw new InvalidOperationException("Login returned empty body");
        _token = body.Token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
    }

    /// <summary>POST /api/admin/reporting-lines/import. Returns (statusCode, body).</summary>
    public Task<(HttpStatusCode Status, string Body)> ImportReportingLinesAsync(object payload, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/api/admin/reporting-lines/import", payload, null, ct);

    /// <summary>POST /api/admin/roles/grant.</summary>
    public Task<(HttpStatusCode Status, string Body)> GrantRoleAsync(object payload, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/api/admin/roles/grant", payload, null, ct);

    /// <summary>GET /api/admin/employee-profiles/{id} → (status, version-from-ETag, body).</summary>
    public async Task<(HttpStatusCode Status, long? Version, string Body)> GetEmployeeProfileAsync(string employeeId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/employee-profiles/{employeeId}");
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        long? version = null;
        if (resp.Headers.ETag is { Tag: { } tag })
        {
            var trimmed = tag.Trim('"');
            if (long.TryParse(trimmed, out var v)) version = v;
        }
        return (resp.StatusCode, version, body);
    }

    /// <summary>PUT /api/admin/employee-profiles/{id} with If-Match: "version".</summary>
    public Task<(HttpStatusCode Status, string Body)> PutEmployeeProfileAsync(string employeeId, object payload, long ifMatchVersion, CancellationToken ct)
        => SendAsync(HttpMethod.Put, $"/api/admin/employee-profiles/{employeeId}", payload, $"\"{ifMatchVersion}\"", ct);

    /// <summary>POST /api/skema/{id}/save.</summary>
    public Task<(HttpStatusCode Status, string Body)> SkemaSaveAsync(string employeeId, object payload, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/skema/{employeeId}/save", payload, null, ct);

    /// <summary>GET /api/skema/{id}/month?year=&amp;month= → (status, count-of-absences-already-recorded).</summary>
    public async Task<(HttpStatusCode Status, int AbsenceCount)> GetSkemaMonthAbsenceCountAsync(string employeeId, int year, int month, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/skema/{employeeId}/month?year={year}&month={month}");
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var count = 0;
        if (resp.StatusCode == HttpStatusCode.OK)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("absences", out var abs) && abs.ValueKind == JsonValueKind.Array)
                    count = abs.GetArrayLength();
            }
            catch (JsonException) { /* leave 0 */ }
        }
        return (resp.StatusCode, count);
    }

    /// <summary>POST /api/approval/submit → (status, periodId-from-body).</summary>
    public async Task<(HttpStatusCode Status, Guid? PeriodId, string Body)> SubmitPeriodAsync(object payload, CancellationToken ct)
    {
        var (status, body) = await SendAsync(HttpMethod.Post, "/api/approval/submit", payload, null, ct);
        Guid? periodId = null;
        if (status == HttpStatusCode.OK)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("periodId", out var pid) && pid.TryGetGuid(out var g))
                    periodId = g;
            }
            catch (JsonException) { /* leave null */ }
        }
        return (status, periodId, body);
    }

    public Task<(HttpStatusCode Status, string Body)> ApprovePeriodAsync(Guid periodId, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/approval/{periodId}/approve", new { }, null, ct);

    public Task<(HttpStatusCode Status, string Body)> RejectPeriodAsync(Guid periodId, string reason, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/approval/{periodId}/reject", new { reason }, null, ct);

    /// <summary>GET /api/admin/reporting-lines/{managerId}/vikar — used as the idempotency probe.</summary>
    public async Task<(HttpStatusCode Status, string Body)> GetVikarAsync(string managerId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/reporting-lines/{managerId}/vikar");
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.StatusCode, body);
    }

    /// <summary>POST /api/admin/reporting-lines/{managerId}/vikar.</summary>
    public Task<(HttpStatusCode Status, string Body)> CreateVikarAsync(string managerId, object payload, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/admin/reporting-lines/{managerId}/vikar", payload, null, ct);

    // ── S114 / TASK-11400 — the unit-spine surface (units → homing → leaders, canonical order) ──

    /// <summary>GET /api/admin/units/forest — the stage-(a) existence probe (envelope { forest: [...] }).</summary>
    public async Task<(HttpStatusCode Status, string Body)> GetUnitsForestAsync(CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/admin/units/forest");
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.StatusCode, body);
    }

    /// <summary>POST /api/admin/units — create one unit (201 body carries the SERVER GUID).</summary>
    public Task<(HttpStatusCode Status, string Body)> CreateUnitAsync(object payload, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/api/admin/units", payload, null, ct);

    /// <summary>GET /api/admin/reporting-lines/tree/{organisationId}/medarbejdere — the unit-tagged
    /// roster (per-person <c>unitId</c>): the stage-(b) already-homed probe.</summary>
    public async Task<(HttpStatusCode Status, string Body)> GetRosterAsync(string organisationId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/reporting-lines/tree/{organisationId}/medarbejdere");
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.StatusCode, body);
    }

    /// <summary>GET /api/admin/users/{userId} → (status, users-row version FROM THE ETag, body).
    /// The FETCHED version feeds the homing PUT's If-Match (never a blanket "1").</summary>
    public async Task<(HttpStatusCode Status, long? Version, string Body)> GetUserAsync(string userId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/users/{userId}");
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        long? version = null;
        if (resp.Headers.ETag is { Tag: { } tag } && long.TryParse(tag.Trim('"'), out var v))
            version = v;
        return (resp.StatusCode, version, body);
    }

    /// <summary>PUT /api/admin/users/{userId}/unit with If-Match: "version" — the SAME-Organisation
    /// unit-assign (homing).</summary>
    public Task<(HttpStatusCode Status, string Body)> PutUserUnitAsync(string userId, object payload, long ifMatchVersion, CancellationToken ct)
        => SendAsync(HttpMethod.Put, $"/api/admin/users/{userId}/unit", payload, $"\"{ifMatchVersion}\"", ct);

    /// <summary>POST /api/admin/units/{unitId}/leaders — designate (200 whether fresh or already
    /// designated; 422s a NON-member, which is why homing must fully precede).</summary>
    public Task<(HttpStatusCode Status, string Body)> DesignateUnitLeaderAsync(Guid unitId, object payload, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/admin/units/{unitId}/leaders", payload, null, ct);

    private async Task<(HttpStatusCode, string)> SendAsync(HttpMethod method, string path, object? payload, string? ifMatch, CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, path);
        if (payload is not null)
            req.Content = JsonContent.Create(payload, options: JsonOpts);
        if (ifMatch is not null)
            req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.StatusCode, body);
    }

    public void Dispose() => _http.Dispose();

    private sealed record LoginResp(string Token);
}
