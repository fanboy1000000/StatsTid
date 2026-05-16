using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S31 / TASK-3102 — Phase 4d-3 Part 1 authoritative store for the three employment-profile
/// fields previously sourced from request payloads (TimeEndpoints) or hardcoded constants
/// (ComplianceEndpoints): <c>weekly_norm_hours</c>, <c>part_time_fraction</c>, and <c>position</c>.
/// Sibling fields (<c>agreement_code</c>, <c>ok_version</c>, <c>employment_category</c>,
/// <c>primary_org_id</c>) stay on the <c>users</c> table per S31 refinement Q3 LEAVE and are
/// joined in at read time so <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>
/// returns a fully-hydrated <see cref="EmploymentProfile"/>.
///
/// <para>
/// <b>S31 scope — data-plane only.</b> The repository is consumed by TASK-3107 admin CRUD,
/// TASK-3108 AdminEndpoints POST extension (4-way atomicity), and TASK-3106
/// EmployeeProfileSeeder. ComplianceEndpoints / BalanceEndpoints / TimeEndpoints / RuleEngine
/// remain UNCHANGED — they keep their current sources until S32 cuts them over atomically
/// with planner-snapshot.
/// </para>
///
/// <para>
/// <b>Versioning shape pre-baked, dormant in S31.</b> The schema includes
/// <c>profile_id UUID PRIMARY KEY</c>, <c>effective_from / effective_to</c>,
/// partial-unique-index <c>(employee_id) WHERE effective_to IS NULL</c>, and history-unique-index
/// <c>(employee_id, effective_from)</c> — all S29 WageTypeMapping / S30 EntitlementConfig
/// precedent. S31 reads and writes ONLY live rows (<c>effective_to IS NULL</c>): no
/// supersession routing, no FOR UPDATE locking — the simple
/// <c>UPDATE ... WHERE employee_id = @id AND effective_to IS NULL AND version = @expected</c>
/// path suffices. S32 will add the ADR-020 D2 3-case supersession routing inside
/// <c>SupersedeAndCreateAsync</c> — that work is deliberately not in S31.
/// </para>
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D5).</b> <see cref="UpsertAsync"/> and
/// <see cref="CreateAsync"/> are <c>(conn, tx)</c> overloads only — the endpoint or seeder
/// owns the transaction, threading audit + outbox writes into the same atomic unit.
/// <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/> is the convenience
/// self-managed overload for non-tx callers (admin GET handler + seeder bootstrap probe).
/// </para>
///
/// <para>
/// <b>ADR-019 admin-strict If-Match.</b> <see cref="UpsertAsync"/> accepts
/// <c>expectedVersion: long?</c>; when supplied, a mismatch against the live row's
/// <c>version</c> column throws <see cref="OptimisticConcurrencyException"/> for the
/// endpoint to map to 412.
/// </para>
///
/// <para>
/// <b><c>IsPartTime</c> is computed, not stored</b> (refinement cycle 2 absorption).
/// The schema does NOT have an <c>is_part_time</c> column;
/// <see cref="EmploymentProfile.IsPartTime"/> is derived as
/// <c>part_time_fraction &lt; 1.0m</c> when constructing the in-memory profile. This
/// eliminates the drift-burden between schema and SharedKernel shape that the original
/// cycle-1 plan carried.
/// </para>
/// </summary>
public sealed class EmployeeProfileRepository
{
    private readonly DbConnectionFactory _dbFactory;

    public EmployeeProfileRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ------------------------------------------------------------------
    // Reads — convenience self-managed overload + in-transaction overload.
    // Both join with `users` so the returned EmploymentProfile is fully hydrated
    // (S31 fields from employee_profiles + sibling fields from users per Q3 LEAVE).
    // ------------------------------------------------------------------

    /// <summary>
    /// S31 / TASK-3102 — convenience read: returns the live (open) employee profile for
    /// <paramref name="employeeId"/>, fully hydrated with sibling fields from the <c>users</c>
    /// table (<see cref="EmploymentProfile.AgreementCode"/>, <see cref="EmploymentProfile.OkVersion"/>,
    /// <see cref="EmploymentProfile.EmploymentCategory"/>, <see cref="EmploymentProfile.OrgId"/>),
    /// or <c>null</c> if no live row exists for the employee.
    ///
    /// <para>
    /// <see cref="EmploymentProfile.IsPartTime"/> is computed as
    /// <c>part_time_fraction &lt; 1.0m</c> per refinement cycle 2 absorption — there is no
    /// <c>is_part_time</c> column in the schema.
    /// </para>
    /// </summary>
    public async Task<EmploymentProfile?> GetByEmployeeIdAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        var hit = await ExecuteGetByEmployeeIdAsync(conn, null, employeeId, ct);
        return hit?.Profile;
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>. Reuses the caller-
    /// supplied <paramref name="conn"/> + <paramref name="tx"/> so the read sits inside the
    /// same transaction as a downstream write (ADR-018 D5 atomic-outbox contract). Used by
    /// admin endpoint handlers that need to read-then-emit-event atomically and by
    /// <see cref="UpsertAsync"/>'s internal preflight when constructing audit payloads.
    /// </summary>
    public async Task<EmploymentProfile?> GetByEmployeeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, CancellationToken ct = default)
    {
        var hit = await ExecuteGetByEmployeeIdAsync(conn, tx, employeeId, ct);
        return hit?.Profile;
    }

    /// <summary>
    /// Step 7a P2 fix — atomic row + version read. The GET endpoint must hand back the row
    /// data and its <c>version</c> from the SAME live snapshot so the ETag it stamps
    /// matches the data it serializes; reading the two in separate statements opens a
    /// concurrency window where the response can carry stale fields with a newer ETag and
    /// the next admin edit would silently overwrite the racing change. Single SELECT;
    /// nullable tuple shape mirrors <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>.
    /// </summary>
    public async Task<(EmploymentProfile Profile, long Version)?> GetByEmployeeIdWithVersionAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetByEmployeeIdAsync(conn, null, employeeId, ct);
    }

    private static async Task<(EmploymentProfile Profile, long Version)?> ExecuteGetByEmployeeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, CancellationToken ct)
    {
        // S31 employee_profiles columns are the source of truth for weekly_norm_hours,
        // part_time_fraction, and position. The sibling fields (agreement_code, ok_version,
        // employment_category, primary_org_id) stay on `users` per refinement Q3 LEAVE and
        // are joined in here so the returned EmploymentProfile is consumable by PCS / rule
        // engine paths unchanged. `ep.version` joins in the row's optimistic-concurrency
        // token for callers that need it on the ETag header (Step 7a P2 fix — same-snapshot
        // read kills the GET race against concurrent admin edits).
        const string sql =
            """
            SELECT
                ep.weekly_norm_hours,
                ep.part_time_fraction,
                ep.position,
                ep.version,
                u.agreement_code,
                u.ok_version,
                u.employment_category,
                u.primary_org_id
            FROM employee_profiles ep
            INNER JOIN users u ON u.user_id = ep.employee_id
            WHERE ep.employee_id = @employeeId
              AND ep.effective_to IS NULL
            """;
        await using var cmd = tx is null
            ? new NpgsqlCommand(sql, conn)
            : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var partTimeFraction = reader.GetDecimal(reader.GetOrdinal("part_time_fraction"));
        var profile = new EmploymentProfile
        {
            EmployeeId = employeeId,
            AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
            OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
            WeeklyNormHours = reader.GetDecimal(reader.GetOrdinal("weekly_norm_hours")),
            EmploymentCategory = reader.GetString(reader.GetOrdinal("employment_category")),
            // S31 cycle 2 absorption: IsPartTime is computed, not stored. No is_part_time
            // column in the schema.
            IsPartTime = partTimeFraction < 1.0m,
            PartTimeFraction = partTimeFraction,
            Position = reader.IsDBNull(reader.GetOrdinal("position"))
                ? null
                : reader.GetString(reader.GetOrdinal("position")),
            OrgId = reader.GetString(reader.GetOrdinal("primary_org_id")),
        };
        var version = reader.GetInt64(reader.GetOrdinal("version"));
        return (profile, version);
    }

    // ------------------------------------------------------------------
    // Writes — atomic-outbox (conn, tx) overloads only (ADR-018 D5).
    // S31 scope: UPDATE the live row (no supersession routing); INSERT a fresh row
    // for net-new employees. Both bump nothing — INSERT writes version=1, UPDATE
    // increments by one. Endpoint owns audit + outbox emission.
    // ------------------------------------------------------------------

    /// <summary>
    /// S31 / TASK-3102 — atomic-outbox INSERT overload for a brand-new live profile row.
    /// Used by TASK-3106 EmployeeProfileSeeder during bootstrap (one row per existing user)
    /// and by TASK-3108 AdminEndpoints POST extension (4-way atomicity: users INSERT +
    /// employee_profiles INSERT + UserCreated outbox + EmployeeProfileCreated outbox, all
    /// in one tx). Writes <c>version = 1</c>, <c>effective_from = '0001-01-01'</c> (schema
    /// default; pre-baked versioning column is dormant in S31), <c>effective_to = NULL</c>.
    /// Caller commits or rolls back the transaction; endpoint emits the audit row + outbox
    /// event in the same tx after this returns.
    ///
    /// <para>
    /// Returns <c>(profile_id, version=1)</c> for the inserted row. The endpoint sets
    /// the wire ETag to <c>"1"</c> on the 201 response.
    /// </para>
    /// </summary>
    /// <exception cref="PostgresException">
    /// Thrown on partial-unique-index conflict (<c>idx_employee_profiles_live</c>) when a
    /// live row already exists for <paramref name="req"/><c>.EmployeeId</c>. The caller
    /// (endpoint) is expected to translate <c>SqlState = "23505"</c> to 409 Conflict; the
    /// seeder should never hit this case because it guards on existing rows.
    /// </exception>
    public async Task<(Guid ProfileId, long Version)> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileCreateRequest req, CancellationToken ct = default)
    {
        // profile_id is generated client-side so the endpoint can include it in the
        // outbox event body (S29 WTM precedent at WageTypeMappingRepository.cs:137).
        var newProfileId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, weekly_norm_hours, part_time_fraction, position,
                effective_from, effective_to, version)
            VALUES (
                @profileId, @employeeId, @weeklyNormHours, @partTimeFraction, @position,
                DEFAULT, NULL, 1)
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", newProfileId);
        cmd.Parameters.AddWithValue("employeeId", req.EmployeeId);
        cmd.Parameters.AddWithValue("weeklyNormHours", req.WeeklyNormHours);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING always yields one row on success.
            throw new InvalidOperationException(
                $"CreateAsync produced no row for employee_id='{req.EmployeeId}'.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// S31 / TASK-3102 — atomic-outbox UPDATE overload for the live row of an existing
    /// employee profile. Used by TASK-3107 admin PUT handler. Caller threads audit + outbox
    /// emission into the same transaction.
    ///
    /// <para>
    /// <b>ADR-019 admin-strict If-Match (optimistic concurrency).</b> When
    /// <paramref name="expectedVersion"/> is non-null, the UPDATE predicate includes
    /// <c>AND version = @expectedVersion</c>. On version mismatch (no rows updated due to
    /// the predicate) the method re-reads the current version to distinguish 412 (stale
    /// If-Match) from 404 (no live row), and throws
    /// <see cref="OptimisticConcurrencyException"/> for the endpoint to map to 412.
    /// When <paramref name="expectedVersion"/> is null, the version predicate is omitted
    /// (used by the seeder + internal callers that don't enforce If-Match).
    /// </para>
    ///
    /// <para>
    /// Increments <c>version</c> by 1 and stamps <c>updated_at = NOW()</c>. Returns
    /// <c>(profile_id, new_version)</c> so the endpoint can build the audit row + outbox
    /// event payload + wire ETag <c>"&lt;new_version&gt;"</c>.
    /// </para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no live row exists for <paramref name="req"/><c>.EmployeeId</c>
    /// (the partial-unique-index guarantees at most one live row per employee; "no rows
    /// updated" combined with "no version mismatch" means there is no live row). Endpoint
    /// maps to 404. Mirrors S29 WTM precedent at
    /// <see cref="WageTypeMappingRepository.UpdateAsync(NpgsqlConnection, NpgsqlTransaction, WageTypeMapping, long, CancellationToken)"/>.
    /// </exception>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when <paramref name="expectedVersion"/> is non-null and does not match the
    /// live row's <c>version</c>. Endpoint maps to 412 per ADR-019. <c>ActualVersion</c>
    /// is populated so the endpoint can return the current state in the response body.
    /// </exception>
    public async Task<(Guid ProfileId, long Version)> UpsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileUpsertRequest req, long? expectedVersion,
        CancellationToken ct = default)
    {
        // S31 scope intentionally narrow: UPDATE the live row, no supersession routing.
        // S32 will add ADR-020 D2 3-case routing inside a new SupersedeAndCreateAsync
        // method modeled on EntitlementConfigRepository.cs:247 / WageTypeMappingRepository.cs:290.
        // The simple WHERE predicate is sufficient because the partial-unique-index
        // (employee_id) WHERE effective_to IS NULL guarantees at most one matching row.
        var sql = expectedVersion is null
            ? """
              UPDATE employee_profiles SET
                  weekly_norm_hours = @weeklyNormHours,
                  part_time_fraction = @partTimeFraction,
                  position = @position,
                  version = version + 1,
                  updated_at = NOW()
              WHERE employee_id = @employeeId
                AND effective_to IS NULL
              RETURNING profile_id, version
              """
            : """
              UPDATE employee_profiles SET
                  weekly_norm_hours = @weeklyNormHours,
                  part_time_fraction = @partTimeFraction,
                  position = @position,
                  version = version + 1,
                  updated_at = NOW()
              WHERE employee_id = @employeeId
                AND effective_to IS NULL
                AND version = @expectedVersion
              RETURNING profile_id, version
              """;
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", req.EmployeeId);
        cmd.Parameters.AddWithValue("weeklyNormHours", req.WeeklyNormHours);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
        if (expectedVersion is not null)
        {
            cmd.Parameters.AddWithValue("expectedVersion", expectedVersion.Value);
        }
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                return (reader.GetGuid(0), reader.GetInt64(1));
            }
        }

        // No row matched the UPDATE predicate. Distinguish 404 (no live row) from 412
        // (stale If-Match) by re-reading the row inside the same tx. The partial-unique-
        // index guarantees there is at most one live row per employee_id, so a single
        // probe suffices.
        await using var probeCmd = new NpgsqlCommand(
            """
            SELECT version FROM employee_profiles
            WHERE employee_id = @employeeId AND effective_to IS NULL
            """, conn, tx);
        probeCmd.Parameters.AddWithValue("employeeId", req.EmployeeId);
        var probeResult = await probeCmd.ExecuteScalarAsync(ct);
        if (probeResult is null || probeResult is DBNull)
        {
            // No live row exists for this employee → 404.
            throw new KeyNotFoundException(
                $"Employee profile not found for employee_id='{req.EmployeeId}'.");
        }

        // Live row exists but version doesn't match → 412 stale If-Match (ADR-019).
        var actualVersion = (long)probeResult;
        throw new OptimisticConcurrencyException(
            $"Employee profile version is {actualVersion}, but caller sent " +
            $"If-Match: \"{expectedVersion?.ToString() ?? "<none>"}\"; refresh and retry.",
            expectedVersion: expectedVersion,
            actualVersion: actualVersion);
    }
}

// ------------------------------------------------------------------
// Request records — Created + Upsert kept separate today for forward-compat with S32
// where Create may take an explicit effective_from (cross-day supersession routing
// per ADR-020 D2). In S31 the shapes are identical.
// ------------------------------------------------------------------

/// <summary>
/// S31 / TASK-3102 — payload for <see cref="EmployeeProfileRepository.UpsertAsync"/>.
/// All three S31-authoritative fields plus the natural key. <see cref="Position"/> is
/// nullable per the schema definition (TEXT NULL).
/// </summary>
public sealed record EmployeeProfileUpsertRequest(
    string EmployeeId,
    decimal WeeklyNormHours,
    decimal PartTimeFraction,
    string? Position);

/// <summary>
/// S31 / TASK-3102 — payload for <see cref="EmployeeProfileRepository.CreateAsync"/>.
/// Kept separate from <see cref="EmployeeProfileUpsertRequest"/> for forward-compat: S32
/// will extend this with an explicit <c>EffectiveFrom</c> field once supersession routing
/// is added. In S31, INSERTs always use the schema default <c>'0001-01-01'</c>.
/// </summary>
public sealed record EmployeeProfileCreateRequest(
    string EmployeeId,
    decimal WeeklyNormHours,
    decimal PartTimeFraction,
    string? Position);
