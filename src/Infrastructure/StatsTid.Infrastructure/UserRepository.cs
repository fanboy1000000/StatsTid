using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class UserRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public UserRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE username = @username AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("username", username);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadUser(reader) : null;
    }

    public async Task<User?> GetByIdAsync(string userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE user_id = @userId AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadUser(reader) : null;
    }

    public async Task<IReadOnlyList<User>> GetByOrgAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE primary_org_id = @orgId AND is_active = TRUE ORDER BY display_name", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        return await ReadUsersAsync(cmd, ct);
    }

    /// <summary>
    /// Non-tx list read returning each user paired with its row-version. Used by the
    /// admin GET-by-org list endpoint at <c>/api/admin/organizations/{orgId}/users</c>
    /// so list rows carry both <c>primaryOrgId</c> (for the table render) and
    /// <c>version</c> (so the frontend list-row type can remain honest about the
    /// row-version field without a per-row follow-up GET). Step 7a cycle 1 absorption
    /// (Codex WARNING-1): closes the list-endpoint contract gap where the table
    /// rendered an undefined <c>primaryOrgId</c> column and the frontend <c>User</c>
    /// type lied about a <c>version</c> field the endpoint never returned. Edit
    /// flow still re-fetches via the per-user GET — list-row <c>version</c> is for
    /// type-honesty and forward-compat, not the source of truth on the next PUT.
    /// </summary>
    public async Task<IReadOnlyList<(User User, long Version)>> GetByOrgWithVersionAsync(
        string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE primary_org_id = @orgId AND is_active = TRUE ORDER BY display_name", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        var rows = new List<(User, long)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var user = ReadUser(reader);
            var version = reader.GetInt64(reader.GetOrdinal("version"));
            rows.Add((user, version));
        }
        return rows;
    }

    /// <summary>
    /// In-tx FOR-UPDATE atomic row + version read — used by <c>AdminEndpoints</c> PUT
    /// <c>/api/admin/users/{userId}</c> to read user fields and their optimistic-concurrency
    /// <c>version</c> token under a row lock as part of the admin-strict If-Match contract
    /// per ADR-019 D2. Mirrors the S31
    /// <see cref="EmployeeProfileRepository.GetByEmployeeIdWithVersionAsync(string, CancellationToken)"/>
    /// in-tx overload precedent.
    ///
    /// <para>
    /// The <c>FOR UPDATE</c> clause prevents the audit-trail race where a pre-transaction
    /// snapshot of the row becomes stale by the time the UPDATE commits — the predecessor
    /// row state captured under this lock is canonical for both the <c>If-Match</c>
    /// precondition check and the <c>UserUpdated</c> audit payload's <c>old_*</c> fields.
    /// </para>
    ///
    /// <para>
    /// Returns <c>null</c> when <c>user_id</c> is not found OR <c>is_active = FALSE</c>
    /// (matches the existing <see cref="GetByIdAsync(string, CancellationToken)"/> semantic;
    /// soft-deleted users are not addressable through admin edit).
    /// </para>
    /// </summary>
    public async Task<User?> GetByIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE user_id = @userId AND is_active = TRUE", conn, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadUser(reader) : null;
    }

    public async Task<(User User, long Version)?> GetByIdWithVersionAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE user_id = @userId AND is_active = TRUE FOR UPDATE",
            conn, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var user = ReadUser(reader);
        var version = reader.GetInt64(reader.GetOrdinal("version"));
        return (user, version);
    }

    /// <summary>
    /// Non-tx atomic row + version read — used by <c>AdminEndpoints</c> GET
    /// <c>/api/admin/users/{userId}</c> (added in TASK-3506) to stamp the response ETag
    /// from the same live snapshot it serializes. Mirrors the S31
    /// <see cref="EmployeeProfileRepository.GetByEmployeeIdWithVersionAsync(string, CancellationToken)"/>
    /// non-tx overload precedent at <c>EmployeeProfileRepository.cs:157</c>.
    ///
    /// <para>
    /// Closes the S31 GET-race class: pre-S31 the GET handler would issue two separate
    /// reads (the existing <see cref="GetByIdAsync(string, CancellationToken)"/> for body
    /// fields plus a follow-up <c>SELECT version</c>) and could return stale fields paired
    /// with a newer ETag — the next admin PUT carrying that ETag in <c>If-Match</c> would
    /// pass the precondition check and silently overwrite the racing change. A single
    /// SELECT eliminates the window.
    /// </para>
    ///
    /// <para>
    /// No <c>FOR UPDATE</c> — this is the read-only GET path, no transaction. Returns
    /// <c>null</c> when <c>user_id</c> is not found OR <c>is_active = FALSE</c> (matches
    /// the existing <see cref="GetByIdAsync(string, CancellationToken)"/> semantic).
    /// </para>
    /// </summary>
    public async Task<(User User, long Version)?> GetByIdWithVersionAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE user_id = @userId AND is_active = TRUE",
            conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var user = ReadUser(reader);
        var version = reader.GetInt64(reader.GetOrdinal("version"));
        return (user, version);
    }

    /// <summary>
    /// S59 / TASK-5906 / ADR-029 — HR-only write of <c>users.birth_date</c> (GDPR PII).
    /// In-tx <c>(conn, tx)</c> overload so the admin DOB endpoint can ride the UPDATE,
    /// the <c>users_audit</c> row, and (no DOB event this sprint — erasure deferred with
    /// ADR-025 D3) in one atomic unit per ADR-018 D3.
    ///
    /// <para>
    /// <b>Admin-strict If-Match (ADR-019 D2).</b> Version-guarded: the UPDATE matches on
    /// <c>version = @expectedVersion</c> and bumps <c>version + 1</c> (ADR-018 D7 row-version
    /// contract — same column the AdminEndpoints user PUT bumps). The caller is expected to
    /// have FOR-UPDATE'd + version-checked the row first (via
    /// <see cref="GetByIdWithVersionAsync(NpgsqlConnection, NpgsqlTransaction, string, CancellationToken)"/>);
    /// the <c>AND version = @expectedVersion</c> predicate here is defense-in-depth. Returns
    /// the new version. Throws <see cref="OptimisticConcurrencyException"/> when no live row
    /// matched the expected version (caller maps to 412); throws
    /// <see cref="KeyNotFoundException"/> when no live row exists at all (caller maps to 404).
    /// </para>
    ///
    /// <para>
    /// <paramref name="birthDate"/> may be <c>null</c> (clearing an unknown DOB).
    /// </para>
    /// </summary>
    public async Task<long> SetBirthDateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string userId, DateOnly? birthDate, long expectedVersion, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users
               SET birth_date = @birthDate,
                   version = version + 1,
                   updated_at = NOW()
             WHERE user_id = @userId
               AND is_active = TRUE
               AND version = @expectedVersion
            RETURNING version
            """, conn, tx);
        cmd.Parameters.AddWithValue("birthDate", (object?)birthDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is long newVersion)
            return newVersion;

        // No row updated — distinguish "row gone" from "version mismatch" so the
        // endpoint can map 404 vs 412 (mirrors EmployeeProfileRepository.SoftDeleteAsync).
        await using var probeCmd = new NpgsqlCommand(
            "SELECT version FROM users WHERE user_id = @userId AND is_active = TRUE", conn, tx);
        probeCmd.Parameters.AddWithValue("userId", userId);
        var actual = await probeCmd.ExecuteScalarAsync(ct);
        if (actual is long actualVersion)
            throw new OptimisticConcurrencyException(
                $"User '{userId}' version is {actualVersion}, but caller sent " +
                $"If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: actualVersion);

        throw new KeyNotFoundException($"User '{userId}' not found or inactive.");
    }

    /// <summary>
    /// S60 / TASK-6004 / ADR-030 — HR-only write of <c>users.employment_start_date</c>.
    /// In-tx <c>(conn, tx)</c> overload so the admin employment-start endpoint can ride the
    /// UPDATE and the <c>users_audit</c> row in one atomic unit per ADR-018 D3. Mirrors the
    /// S59 <see cref="SetBirthDateAsync(NpgsqlConnection, NpgsqlTransaction, string, DateOnly?, long, CancellationToken)"/>
    /// precedent exactly.
    ///
    /// <para>
    /// <b>Admin-strict If-Match (ADR-019 D2).</b> Version-guarded: the UPDATE matches on
    /// <c>version = @expectedVersion</c> and bumps <c>version + 1</c> (ADR-018 D7 row-version
    /// contract). The caller is expected to have FOR-UPDATE'd + version-checked the row first
    /// (via <see cref="GetByIdWithVersionAsync(NpgsqlConnection, NpgsqlTransaction, string, CancellationToken)"/>);
    /// the <c>AND version = @expectedVersion</c> predicate here is defense-in-depth. Returns
    /// the new version. Throws <see cref="OptimisticConcurrencyException"/> when no live row
    /// matched the expected version (caller maps to 412); throws
    /// <see cref="KeyNotFoundException"/> when no live row exists at all (caller maps to 404).
    /// </para>
    ///
    /// <para>
    /// <paramref name="employmentStartDate"/> may be <c>null</c> (clearing an unknown start date).
    /// </para>
    /// </summary>
    public async Task<long> SetEmploymentStartDateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string userId, DateOnly? employmentStartDate, long expectedVersion, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users
               SET employment_start_date = @employmentStartDate,
                   version = version + 1,
                   updated_at = NOW()
             WHERE user_id = @userId
               AND is_active = TRUE
               AND version = @expectedVersion
            RETURNING version
            """, conn, tx);
        cmd.Parameters.AddWithValue("employmentStartDate", (object?)employmentStartDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is long newVersion)
            return newVersion;

        // No row updated — distinguish "row gone" from "version mismatch" so the
        // endpoint can map 404 vs 412 (mirrors SetBirthDateAsync / SoftDeleteAsync).
        await using var probeCmd = new NpgsqlCommand(
            "SELECT version FROM users WHERE user_id = @userId AND is_active = TRUE", conn, tx);
        probeCmd.Parameters.AddWithValue("userId", userId);
        var actual = await probeCmd.ExecuteScalarAsync(ct);
        if (actual is long actualVersion)
            throw new OptimisticConcurrencyException(
                $"User '{userId}' version is {actualVersion}, but caller sent " +
                $"If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: actualVersion);

        throw new KeyNotFoundException($"User '{userId}' not found or inactive.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // S70 / TASK-7003 / ADR-033 slice 3a (SPRINT-70 R9a) — terminated-INCLUSIVE reads
    // and the end-date guarded write. ADDITIVE ONLY: every active-only method above is
    // untouched; the shared `is_active = TRUE` soft-delete semantic (see GetByIdAsync)
    // remains the default for ALL general read/mutation paths. These counterparts exist
    // EXCLUSIVELY for the SPRINT-70 R9c allowlist surfaces (the admin employment-end-date
    // set/clear endpoint [TASK-7002], the settlement manual-resolve + reconcile-payout
    // endpoints, the year-overview read) and the R9d settlement pass (TASK-7004) —
    // a leaver is deactivated by the R2 Step-A flip BEFORE settlement, so those surfaces
    // must be able to address an `is_active = FALSE` row. Do NOT call these from any
    // other path without an explicit allowlist ruling.
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// S70 / TASK-7003 (SPRINT-70 R9a) — standalone by-id read with NO <c>is_active</c>
    /// filter (terminated/deactivated rows ARE returned). Same row mapping as
    /// <see cref="GetByIdAsync(string, CancellationToken)"/>.
    ///
    /// <para>
    /// <b>Allowlist-restricted (R9c):</b> for the SPRINT-70 terminated-aware surfaces only
    /// (end-date endpoint, settlement resolve/reconcile-payout, year-overview via
    /// <c>OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync</c>). The general
    /// read paths stay active-only — a deactivated user must remain unaddressable everywhere
    /// else (B2/S68 origin: HR could not reach a departed employee's settlement surfaces;
    /// the fix is additive, NOT a relaxation of the shared filter).
    /// </para>
    /// </summary>
    public async Task<User?> GetByIdIncludingTerminatedAsync(string userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadUser(reader) : null;
    }

    /// <summary>
    /// S70 / TASK-7003 (SPRINT-70 R9a/R9d) — in-tx <c>(conn, tx)</c> by-id read with NO
    /// <c>is_active</c> filter. Mirrors the in-tx
    /// <see cref="GetByIdAsync(NpgsqlConnection, NpgsqlTransaction, string, CancellationToken)"/>
    /// minus the filter; no <c>FOR UPDATE</c> (read-only participant — the row-locking
    /// counterpart is
    /// <see cref="GetByIdWithVersionIncludingTerminatedAsync(NpgsqlConnection, NpgsqlTransaction, string, CancellationToken)"/>).
    ///
    /// <para>
    /// <b>Allowlist-restricted (R9c/R9d):</b> built for TASK-7004's settlement pass —
    /// <c>VacationSettlementService.SettleAsync</c> reads the employee inside the advisory-locked
    /// settlement transaction, and Step B always runs AFTER Step A's deactivation flip, so the
    /// pass must read the leaver UNCONDITIONALLY through this overload (R9d: not keyed on
    /// <c>trigger == TERMINATION</c> — the active-employee case is a strict subset; the pass is
    /// system-internal, access control does not ride the <c>is_active</c> filter). General
    /// in-tx reads stay on the filtered overload.
    /// </para>
    /// </summary>
    public async Task<User?> GetByIdIncludingTerminatedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE user_id = @userId", conn, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadUser(reader) : null;
    }

    /// <summary>
    /// S70 / TASK-7003 (SPRINT-70 R9a) — in-tx FOR-UPDATE atomic row + version read with NO
    /// <c>is_active</c> filter. Mirrors
    /// <see cref="GetByIdWithVersionAsync(NpgsqlConnection, NpgsqlTransaction, string, CancellationToken)"/>
    /// (the ADR-019 D2 admin-strict If-Match read-under-lock shape) minus the filter.
    ///
    /// <para>
    /// <b>Allowlist-restricted (R9c):</b> for TASK-7002's employment-end-date set/clear endpoint
    /// ONLY — without this the endpoint could never read, correct, or clear a deactivated
    /// leaver's end date (the existing If-Match probes filter <c>is_active</c> and dead-end at
    /// 404 the moment the R2 flip lands). R1(c) reactivation (clear with
    /// <c>end_date_deactivated = TRUE</c>) MUST be able to FOR-UPDATE the inactive row it
    /// reactivates. General admin edit paths stay on the filtered overload (soft-deleted users
    /// are not addressable through admin edit).
    /// </para>
    /// </summary>
    public async Task<(User User, long Version)?> GetByIdWithVersionIncludingTerminatedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE user_id = @userId FOR UPDATE",
            conn, tx);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var user = ReadUser(reader);
        var version = reader.GetInt64(reader.GetOrdinal("version"));
        return (user, version);
    }

    /// <summary>
    /// S70 / TASK-7003 (SPRINT-70 R9a) — non-tx atomic row + version read with NO
    /// <c>is_active</c> filter. Mirrors <see cref="GetByIdWithVersionAsync(string, CancellationToken)"/>
    /// (the single-SELECT ETag-stamping GET shape that closes the S31 GET-race class) minus the
    /// filter; no <c>FOR UPDATE</c> (read-only GET path, no transaction).
    ///
    /// <para>
    /// <b>Allowlist-restricted (R9c):</b> for TASK-7002's end-date GET/ETag surface only — an HR
    /// operator must be able to fetch a deactivated leaver's current end date + a coherent
    /// If-Match token before correcting or clearing it (R1(c) is the ONLY reactivation path for
    /// lifecycle-deactivated leavers). General GET paths stay on the filtered overload.
    /// </para>
    /// </summary>
    public async Task<(User User, long Version)?> GetByIdWithVersionIncludingTerminatedAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM users WHERE user_id = @userId",
            conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var user = ReadUser(reader);
        var version = reader.GetInt64(reader.GetOrdinal("version"));
        return (user, version);
    }

    /// <summary>
    /// S70 / TASK-7003 (SPRINT-70 R9a, consumed by TASK-7002 per R1) — guarded write of the
    /// employment-end-date lifecycle tuple (<c>employment_end_date</c>,
    /// <c>end_date_deactivated</c>, <c>is_active</c>) with NO <c>is_active</c> filter. In-tx
    /// <c>(conn, tx)</c> so the endpoint rides the UPDATE, the R10
    /// <c>EmployeeEmploymentEndDateSet</c> outbox emission, the R1(e) deactivation side effects,
    /// and the audit row in ONE atomic unit per ADR-018 D3 — all under the R12 employee advisory
    /// lock the CALLER acquires first.
    ///
    /// <para>
    /// <b>Admin-strict If-Match (ADR-019 D2).</b> Version-guarded: matches on
    /// <c>version = @expectedVersion</c> and bumps <c>version + 1</c> (ADR-018 D7 — a held ETag
    /// must not survive a lifecycle transition). The caller is expected to have FOR-UPDATE'd +
    /// version-checked the row first via
    /// <see cref="GetByIdWithVersionIncludingTerminatedAsync(NpgsqlConnection, NpgsqlTransaction, string, CancellationToken)"/>;
    /// the version predicate here is defense-in-depth. Returns the new version. Throws
    /// <see cref="OptimisticConcurrencyException"/> when a row exists but no version matched
    /// (caller maps to 412); throws <see cref="KeyNotFoundException"/> when no row exists at all
    /// (caller maps to 404). Unlike the active-only Set* shapes above, the probe carries no
    /// <c>is_active</c> filter — a deactivated leaver's row is a legitimate edit subject here.
    /// </para>
    ///
    /// <para>
    /// <b>The caller computes the R1 lifecycle outcome; this method only persists it.</b>
    /// R1(a) set-already-passed → (<c>endDate</c>, <c>endDateDeactivated: true</c>,
    /// <c>isActive: false</c>); R1(b) future-dated → (<c>endDate</c>, <c>false</c>, current
    /// <c>is_active</c>); R1(c) clear-with-provenance → (<c>null</c>, <c>false</c>,
    /// <c>true</c>); R1(c)/(d) clear/set on a manually-deactivated row → provenance NOT claimed
    /// (<c>endDateDeactivated: false</c>, <c>isActive</c> unchanged). The repository deliberately
    /// does not re-derive any of this (deterministic single-writer: the endpoint's locked
    /// read-decide-write).
    /// </para>
    ///
    /// <para>
    /// <b>Allowlist-restricted (R9c):</b> TASK-7002's end-date endpoint is the ONLY caller
    /// (the R2 Step-A poller flip has its own predicate-guarded UPDATE in
    /// <c>SettlementCloseService</c>, TASK-7005). General mutation paths stay active-only.
    /// </para>
    /// </summary>
    public async Task<long> SetEmploymentEndDateIncludingTerminatedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string userId, DateOnly? employmentEndDate, bool endDateDeactivated, bool isActive,
        long expectedVersion, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users
               SET employment_end_date = @employmentEndDate,
                   end_date_deactivated = @endDateDeactivated,
                   is_active = @isActive,
                   version = version + 1,
                   updated_at = NOW()
             WHERE user_id = @userId
               AND version = @expectedVersion
            RETURNING version
            """, conn, tx);
        cmd.Parameters.AddWithValue("employmentEndDate", (object?)employmentEndDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("endDateDeactivated", endDateDeactivated);
        cmd.Parameters.AddWithValue("isActive", isActive);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is long newVersion)
            return newVersion;

        // No row updated — distinguish "row gone" from "version mismatch" so the endpoint can
        // map 404 vs 412 (mirrors SetBirthDateAsync / SetEmploymentStartDateAsync, EXCEPT the
        // probe deliberately carries no is_active filter: an inactive leaver's row must probe
        // as a 412 version mismatch, never a false 404).
        await using var probeCmd = new NpgsqlCommand(
            "SELECT version FROM users WHERE user_id = @userId", conn, tx);
        probeCmd.Parameters.AddWithValue("userId", userId);
        var actual = await probeCmd.ExecuteScalarAsync(ct);
        if (actual is long actualVersion)
            throw new OptimisticConcurrencyException(
                $"User '{userId}' version is {actualVersion}, but caller sent " +
                $"If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: actualVersion);

        throw new KeyNotFoundException($"User '{userId}' not found.");
    }

    private static async Task<IReadOnlyList<User>> ReadUsersAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var users = new List<User>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            users.Add(ReadUser(reader));
        return users;
    }

    private static User ReadUser(NpgsqlDataReader reader) => new()
    {
        UserId = reader.GetString(reader.GetOrdinal("user_id")),
        Username = reader.GetString(reader.GetOrdinal("username")),
        PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
        DisplayName = reader.GetString(reader.GetOrdinal("display_name")),
        Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString(reader.GetOrdinal("email")),
        PrimaryOrgId = reader.GetString(reader.GetOrdinal("primary_org_id")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        EmploymentCategory = reader.GetString(reader.GetOrdinal("employment_category")),
        // S59 / ADR-029 — DOB read-model plumbing (GDPR PII). Read so the Skema save
        // path (TASK-5907) can derive senior age. NULLABLE. Never surfaced to non-HR
        // callers — the admin user projections in AdminEndpoints deliberately omit it.
        BirthDate = reader.IsDBNull(reader.GetOrdinal("birth_date"))
            ? null
            : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("birth_date")),
        // S60 / ADR-030 — employment-start read-model plumbing (HR-scoped). Read so the
        // Skema/Balance accrual seams (TASK-6005) can pro-rate earned-to-date for mid-year
        // hires. NULLABLE. Never surfaced to non-HR callers — the admin user projections
        // in AdminEndpoints deliberately omit it (same handling as birth_date).
        EmploymentStartDate = reader.IsDBNull(reader.GetOrdinal("employment_start_date"))
            ? null
            : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("employment_start_date")),
        // S70 / ADR-033 slice 3a (SPRINT-70 R1) — employment-end lifecycle read-model plumbing
        // (HR-scoped; TASK-7002 end-date endpoint + the Step-A/Step-B lifecycle read it).
        // NULLABLE date + the deactivation-provenance flag. Never surfaced to non-HR callers —
        // the admin user projections in AdminEndpoints deliberately omit both (same handling
        // as birth_date / employment_start_date).
        EmploymentEndDate = reader.IsDBNull(reader.GetOrdinal("employment_end_date"))
            ? null
            : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("employment_end_date")),
        EndDateDeactivated = reader.GetBoolean(reader.GetOrdinal("end_date_deactivated")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
