using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Tests.Regression.TestSupport;

/// <summary>
/// S64 / TASK-6402 — shared regression seed helper. Atomically writes the
/// three rows a dated <see cref="EmploymentProfileResolver"/> lookup requires:
/// <c>users</c> + <c>user_agreement_codes</c> + <c>employee_profiles</c>.
///
/// <para>
/// <b>Why this exists.</b> The resolver enforces the S34 / TASK-3406
/// data-integrity contract: an <c>employee_profiles</c> row covering an
/// as-of date with NO <c>user_agreement_codes</c> row covering the same date
/// throws <see cref="StatsTid.SharedKernel.Exceptions.EmployeeProfileNotFoundException"/>
/// (resolver L161-170, fail-loud). A fixture that seeds users+profiles but
/// forgets the agreement-code row half-seeds that contract and the resolver
/// rejects every lookup. This helper makes that mistake impossible: all three
/// rows land in one place, the agreement-code row anchored at the same
/// history-covering <c>'0001-01-01'</c> default the production backfill seeder
/// (TASK-3403) uses, so any as-of date on or after <paramref name="effectiveFrom"/>
/// resolves cleanly.
/// </para>
///
/// <para>
/// Idempotent (<c>ON CONFLICT DO NOTHING</c> on every row); safe to call more
/// than once per container. The organization FK parent is optionally ensured
/// (<paramref name="ensureOrg"/>, default true) so a caller that has not
/// already seeded <paramref name="orgId"/> still satisfies
/// <c>users.primary_org_id → organizations(org_id)</c>.
/// </para>
/// </summary>
internal static class RegressionSeed
{
    /// <summary>
    /// Connection-string convenience overload. Opens a single connection and
    /// delegates to <see cref="SeedEmployeeAsync(NpgsqlConnection, string, string, string, string, decimal, DateOnly, string?, bool, CancellationToken)"/>.
    /// </summary>
    public static async Task SeedEmployeeAsync(
        string connectionString,
        string employeeId,
        string orgId,
        string agreementCode = "AC",
        string okVersion = "OK24",
        decimal partTimeFraction = 1.000m,
        DateOnly? effectiveFrom = null,
        string? position = null,
        bool ensureOrg = true,
        CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await SeedEmployeeAsync(
            conn, employeeId, orgId, agreementCode, okVersion,
            partTimeFraction, effectiveFrom ?? DefaultEffectiveFrom, position, ensureOrg, ct);
    }

    /// <summary>
    /// Atomically writes the three resolver-required rows on the supplied open
    /// connection. The caller owns the connection lifetime.
    /// </summary>
    /// <param name="conn">An OPEN connection. Not enrolled in any caller transaction —
    /// each statement auto-commits; callers needing rollback semantics should seed
    /// outside their unit-under-test transaction (the established fixture pattern).</param>
    /// <param name="employeeId">Shared identity for <c>users.user_id</c>,
    /// <c>employee_profiles.employee_id</c>, and <c>user_agreement_codes.user_id</c>
    /// (the resolver JOIN treats them as one identifier).</param>
    /// <param name="orgId"><c>users.primary_org_id</c> FK target.</param>
    /// <param name="agreementCode">Dated agreement code written to
    /// <c>user_agreement_codes</c> AND cached on <c>users.agreement_code</c>
    /// (mirrors the production canonical-write contract).</param>
    /// <param name="okVersion"><c>users.ok_version</c> + <c>organizations.ok_version</c>.</param>
    /// <param name="partTimeFraction"><c>employee_profiles.part_time_fraction</c>.</param>
    /// <param name="effectiveFrom">History-covering anchor for BOTH the profile and the
    /// agreement-code rows. Defaults to <c>'0001-01-01'</c> so every as-of date resolves.</param>
    /// <param name="position">Optional <c>employee_profiles.position</c>.</param>
    /// <param name="ensureOrg">When true (default) the organization row is upserted first.</param>
    public static async Task SeedEmployeeAsync(
        NpgsqlConnection conn,
        string employeeId,
        string orgId,
        string agreementCode = "AC",
        string okVersion = "OK24",
        decimal partTimeFraction = 1.000m,
        DateOnly? effectiveFrom = null,
        string? position = null,
        bool ensureOrg = true,
        CancellationToken ct = default)
    {
        var from = effectiveFrom ?? DefaultEffectiveFrom;

        if (ensureOrg)
        {
            await using var orgCmd = new NpgsqlCommand(
                """
                INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                           materialized_path, agreement_code, ok_version)
                VALUES (@orgId, @orgName, 'ORGANISATION', NULL, @path, @agreementCode, @okVersion)
                ON CONFLICT (org_id) DO NOTHING
                """, conn);
            orgCmd.Parameters.AddWithValue("orgId", orgId);
            orgCmd.Parameters.AddWithValue("orgName", $"{orgId} Test Org");
            orgCmd.Parameters.AddWithValue("path", $"/{orgId}/");
            orgCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            orgCmd.Parameters.AddWithValue("okVersion", okVersion);
            await orgCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version)
            VALUES (@id, @id, 'dev-only', @displayName, NULL,
                    @orgId, @agreementCode, @okVersion)
            ON CONFLICT (user_id) DO NOTHING
            """, conn))
        {
            userCmd.Parameters.AddWithValue("id", employeeId);
            userCmd.Parameters.AddWithValue("displayName", $"Seeded {employeeId}");
            userCmd.Parameters.AddWithValue("orgId", orgId);
            userCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            userCmd.Parameters.AddWithValue("okVersion", okVersion);
            await userCmd.ExecuteNonQueryAsync(ct);
        }

        // employee_profiles — dated row covering [effectiveFrom, ∞).
        await using (var epCmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version)
            VALUES (
                gen_random_uuid(), @id, @partTimeFraction, @position,
                @effectiveFrom, NULL, 1)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            epCmd.Parameters.AddWithValue("id", employeeId);
            epCmd.Parameters.AddWithValue("partTimeFraction", partTimeFraction);
            epCmd.Parameters.AddWithValue("position", (object?)position ?? DBNull.Value);
            epCmd.Parameters.AddWithValue("effectiveFrom", from);
            await epCmd.ExecuteNonQueryAsync(ct);
        }

        // user_agreement_codes — the row the S34 resolver contract requires.
        // Anchored at the SAME effectiveFrom so it covers every date the profile does.
        await using (var uacCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (
                assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (
                gen_random_uuid(), @id, @agreementCode, @effectiveFrom, NULL, 1)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            uacCmd.Parameters.AddWithValue("id", employeeId);
            uacCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            uacCmd.Parameters.AddWithValue("effectiveFrom", from);
            await uacCmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// History-covering anchor matching the production backfill seeder
    /// (TASK-3403 seeds <c>user_agreement_codes</c> at <c>'0001-01-01'</c>)
    /// and the <c>employee_profiles.effective_from</c> schema default.
    /// </summary>
    private static readonly DateOnly DefaultEffectiveFrom = new(1, 1, 1);
}
