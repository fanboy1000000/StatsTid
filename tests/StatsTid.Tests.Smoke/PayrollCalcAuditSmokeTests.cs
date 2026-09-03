using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace StatsTid.Tests.Smoke;

/// <summary>
/// S134 TASK-13404 (QUAL-003) — composed-stack probe that the payroll/calc host now writes a
/// manifest-linked <c>audit_log</c> row (the ADR-016 D10 <c>segment_manifests⋈audit_log</c> linkage).
///
/// <para>
/// <b>Why (for a product manager):</b> QUAL-003 found that a real calculate-and-export call through
/// the payroll service left NO audit trail — the host had no audit middleware registered, and the
/// method that tags an audit row with the calculation's manifest id was never called. This probe is
/// the standing proof that a real call against the running stack now (a) writes exactly one audit row
/// tagged with THAT call's manifest id, and (b) that the manifest⋈audit join promised by ADR-016 D10
/// actually returns it. The unit/regression suites stub the rule engine and never register the
/// middleware, so ONLY a composed-stack probe can see this end-to-end wiring (same rationale as the
/// S73 backend→rule-engine hop probe in <see cref="SmokeTests"/>).
/// </para>
///
/// <para>
/// <b>Requires the Docker Compose stack (docker/docker-compose.yml) up:</b> the payroll service at
/// <c>:5400</c>, the rule engine, and Postgres exposed at <c>localhost:5432</c>. CI-gated — this is
/// not asserted green from a dev box without the stack. Like the other smoke tests it fails (rather
/// than skips) when the stack is absent, so a broken stack surfaces loudly.
/// </para>
///
/// <para>
/// <b>Self-isolation.</b> The probe seeds only what the calc path requires and does so idempotently:
/// a live <c>employee_profiles</c> row + a covering <c>user_agreement_codes</c> row for the target
/// (both normally backfilled by the backend seeder on startup — seeded here belt-and-braces so the
/// probe does not depend on that), and an <b>APPROVED</b> <c>approval_periods</c> row (the endpoint's
/// approval guard). It targets the init.sql-seeded <c>emp001</c> (STY01, AC, OK24). Reruns are safe:
/// each calc mints a UNIQUE manifest id, and the probe identifies THIS run's manifest by the
/// <c>segment_manifests</c> row created after a pre-call high-water timestamp — so the
/// manifest-FILTERED assertions hold on a lived-in database regardless of prior runs.
/// </para>
/// </summary>
public sealed class PayrollCalcAuditSmokeTests
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string PayrollUrl = "http://localhost:5400";

    // The composed Postgres (docker/docker-compose.yml: postgres → 5432:5432, db/user/pwd below).
    private const string PgConnString =
        "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

    // Matches the dev signing key in docker/docker-compose.yml (x-jwt-env).
    private const string JwtSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // emp001 (Jesper Andersen) — init.sql-seeded in STY01, agreement AC, OK24.
    private const string EmployeeId = "emp001";
    private const string OrgId = "STY01";

    [Fact]
    public async Task PayrollCalculateAndExport_WritesExactlyOneManifestLinkedAuditRow_ComposedStack()
    {
        // A single-OK-version month: every day is < 2026-04-01 ⇒ OK24, so the legacy planless plan is
        // one segment (no interior OK boundary → the AlignedWindow overtime rule does not reject).
        var periodStart = new DateOnly(2026, 3, 1);
        var periodEnd = new DateOnly(2026, 3, 31);

        await using var db = new NpgsqlConnection(PgConnString);
        await db.OpenAsync();

        // ── Seed the calc path's prerequisites (idempotent) ──
        await EnsureEmployeeProvisionedAsync(db, EmployeeId);
        await EnsureApprovedPeriodAsync(db, EmployeeId, OrgId, periodStart, periodEnd);

        // High-water mark BEFORE the call, DB-side, so THIS run's manifest is identifiable on a
        // lived-in database where earlier runs left their own segment_manifests rows.
        var t0 = await ScalarAsync<DateTime>(db, "SELECT NOW()");

        // ── Drive the calc endpoint ──
        var request = new
        {
            profile = new
            {
                employeeId = EmployeeId,
                agreementCode = "AC",
                okVersion = "OK24",
                employmentCategory = "Standard",
                partTimeFraction = 1.0m,
                orgId = OrgId,
            },
            // A full week of weekday 7.4h entries (Mon 2026-03-02 .. Fri 2026-03-06). Enough for the
            // calc to run to manifest emission; the probe does not depend on export lines existing.
            entries = new[]
            {
                new { employeeId = EmployeeId, date = "2026-03-02", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = EmployeeId, date = "2026-03-03", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = EmployeeId, date = "2026-03-04", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = EmployeeId, date = "2026-03-05", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = EmployeeId, date = "2026-03-06", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
            },
            absences = Array.Empty<object>(),
            periodStart = "2026-03-01",
            periodEnd = "2026-03-31",
            previousFlexBalance = 0m,
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{PayrollUrl}/api/payroll/calculate-and-export")
        {
            Content = JsonContent.Create(request),
        };
        // GlobalAdmin bearer: bypasses the org-scope guard (GLOBAL scope short-circuits) and is
        // forwarded by the payroll host to the rule engine, so per-segment rule evaluation succeeds.
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", GenerateGlobalAdminToken());

        var response = await _client.SendAsync(httpRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // ── Identify THIS call's manifest id: the segment_manifests row for (emp, period) created
        //    after t0. A manifest is emitted on any successful (non-total-failure) forward calc. ──
        var manifestId = await ScalarOrNullAsync<Guid>(
            db,
            """
            SELECT manifest_id
            FROM segment_manifests
            WHERE employee_id = @emp AND period_start = @ps AND period_end = @pe AND created_at >= @t0
            ORDER BY created_at DESC
            LIMIT 1
            """,
            ("emp", EmployeeId), ("ps", periodStart), ("pe", periodEnd), ("t0", t0));
        Assert.True(manifestId.HasValue,
            "No segment_manifests row was created for this calc — the manifest was not persisted.");

        var manifestText = manifestId.Value.ToString();

        // ── (1) EXACTLY ONE audit_log row FILTERED by THIS calc's manifest id (query details) ──
        // NOT a raw row count: the middleware also audits the OTHER in-request payroll HTTP calls, and
        // only /calculate-and-export stamps a manifest id — so filtering audit_log.details on THIS
        // manifest id isolates precisely the one row this calc produced. (RetroactiveCorrectionService
        // writes audit_projection, a DIFFERENT table, and only on /recalculate — irrelevant here.)
        //
        // Poll briefly: the audit row is written by the middleware AFTER the endpoint body runs
        // (post-_next), which can lag the flushed HTTP response by a few milliseconds. The poll returns
        // on the FIRST observation of the row, so a unique manifest yields exactly 1.
        var manifestFilteredAuditRows = await PollAuditRowCountAsync(db, manifestText, TimeSpan.FromSeconds(10));
        Assert.Equal(1L, manifestFilteredAuditRows);

        // ── (2) The ADR-016 D10 join returns that one row ──
        // segment_manifests ⋈ audit_log on manifest_id (audit_log stores it as text in details JSONB;
        // ADR-vs-code drift noted in the task — the probe joins on details, matching the code).
        var joinedRows = await ScalarAsync<long>(
            db,
            """
            SELECT COUNT(*)
            FROM segment_manifests sm
            JOIN audit_log al ON al.details->>'manifest_id' = sm.manifest_id::text
            WHERE sm.manifest_id = @m
            """,
            ("m", manifestId.Value));
        Assert.Equal(1L, joinedRows);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Seeding (idempotent)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures the target has a live dated employment profile + a covering dated agreement-code row —
    /// the two dated reads the payroll calc's per-segment <c>EmploymentProfileResolver</c> performs.
    /// Both are normally backfilled by the backend seeder on startup; seeded here (only when absent)
    /// so the probe does not silently depend on seeder ordering.
    /// </summary>
    private static async Task EnsureEmployeeProvisionedAsync(NpgsqlConnection conn, string employeeId)
    {
        await ExecAsync(conn,
            """
            INSERT INTO employee_profiles (employee_id, part_time_fraction, position, effective_from, effective_to,
                                           employment_category)
            SELECT @emp, 1.000, NULL, DATE '0001-01-01', NULL,
                   (SELECT u.employment_category FROM users u WHERE u.user_id = @emp)
            WHERE NOT EXISTS (
                SELECT 1 FROM employee_profiles WHERE employee_id = @emp AND effective_to IS NULL)
            """,
            ("emp", employeeId));

        await ExecAsync(conn,
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to)
            SELECT gen_random_uuid(), @emp, 'AC', DATE '0001-01-01', NULL
            WHERE NOT EXISTS (SELECT 1 FROM user_agreement_codes WHERE user_id = @emp)
            """,
            ("emp", employeeId));
    }

    /// <summary>
    /// Ensures an APPROVED <c>approval_periods</c> row for (employee, period) — the endpoint's approval
    /// guard requires status='APPROVED'. Idempotent via the (employee_id, period_start, period_end)
    /// unique key: a re-run forces the row back to APPROVED.
    /// </summary>
    private static async Task EnsureApprovedPeriodAsync(
        NpgsqlConnection conn, string employeeId, string orgId, DateOnly periodStart, DateOnly periodEnd)
    {
        await ExecAsync(conn,
            """
            INSERT INTO approval_periods
                (employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version, approved_by, approved_at)
            VALUES
                (@emp, @org, @ps, @pe, 'MONTHLY', 'APPROVED', 'AC', 'OK24', 'smoke-qual003', NOW())
            ON CONFLICT (employee_id, period_start, period_end)
            DO UPDATE SET status = 'APPROVED', approved_by = 'smoke-qual003', approved_at = NOW()
            """,
            ("emp", employeeId), ("org", orgId), ("ps", periodStart), ("pe", periodEnd));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Tiny ADO helpers
    // ──────────────────────────────────────────────────────────────────────
    // CA2100 is suppressed for these three helpers: every `sql` passed is a hardcoded string literal
    // at the call sites above, and every dynamic value flows through NpgsqlParameter (never string-
    // concatenated) — there is no user-controlled SQL. The `string sql` parameter exists only to reuse
    // one exec/scalar shape across the probe's literal queries.
#pragma warning disable CA2100
    private static async Task ExecAsync(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in parameters)
            cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in parameters)
            cmd.Parameters.AddWithValue(n, v);
        var result = await cmd.ExecuteScalarAsync();
        return (T)result!;
    }

    private static async Task<T?> ScalarOrNullAsync<T>(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
        where T : struct
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in parameters)
            cmd.Parameters.AddWithValue(n, v);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : (T)result;
    }

    /// <summary>
    /// Polls <c>audit_log</c> for rows tagged with <paramref name="manifestText"/> until at least one
    /// appears or <paramref name="timeout"/> elapses. Returns the observed count (0 on timeout, so the
    /// caller's Assert.Equal(1, …) fails with a clear value). Absorbs the small lag between the flushed
    /// HTTP response and the middleware's post-_next audit write.
    /// </summary>
    private static async Task<long> PollAuditRowCountAsync(NpgsqlConnection conn, string manifestText, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long count = 0;
        while (true)
        {
            count = await ScalarAsync<long>(
                conn, "SELECT COUNT(*) FROM audit_log WHERE details->>'manifest_id' = @m", ("m", manifestText));
            if (count >= 1 || DateTime.UtcNow >= deadline)
                return count;
            await Task.Delay(250);
        }
    }
#pragma warning restore CA2100

    // ──────────────────────────────────────────────────────────────────────
    //  JWT (GlobalAdmin) — mirrors SmokeTests.GenerateTestToken
    // ──────────────────────────────────────────────────────────────────────

    private static string GenerateGlobalAdminToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var scopes = JsonSerializer.Serialize(new[]
        {
            new { Role = "GlobalAdmin", OrgId = OrgId, ScopeType = "GLOBAL" },
        });

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", "smoke-qual003-admin"),
                new Claim("role", "GlobalAdmin"),
                new Claim("org_id", OrgId),
                new Claim("agreement_code", "AC"),
                new Claim("scopes", scopes),
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "statstid",
            Audience = "statstid",
            SigningCredentials = credentials,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
