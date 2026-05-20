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
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
