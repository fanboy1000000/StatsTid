using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class OrganizationRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public OrganizationRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Organization?> GetByIdAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM organizations WHERE org_id = @orgId AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadOrg(reader) : null;
    }

    public async Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM organizations WHERE is_active = TRUE ORDER BY materialized_path", conn);
        return await ReadOrgsAsync(cmd, ct);
    }

    // S95 / ADR-035 slice 4 — GetDescendantsAsync (the materialized-path subtree walk) was RETIRED
    // here: it lost its only production caller in S93 (OrgScopeValidator.GetAccessibleOrgsAsync now
    // returns the exact assigned org set, no subtree expansion) and is removed with the rest of the
    // tree-WALK machinery in S95.

    // ──────────────────────────────────────────────────────────────────────
    //  S98 / ADR-035 — GlobalAdmin org-structure ops (soft-delete + re-parent).
    //  In-tx writers: the calling endpoint owns the tx (ADR-018 D3) so the org row
    //  flip / re-parent commits atomically with the OrganizationDeleted/Moved event
    //  + the audit-projection row.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>S98 — locks the ACTIVE org row (<c>SELECT … WHERE org_id=@id AND is_active=TRUE
    /// FOR UPDATE</c>) on the caller's tx, serializing concurrent structural ops on the same org.
    /// Returns the locked <see cref="Organization"/>, or <c>null</c> when the org is absent or
    /// already soft-deleted (the endpoint maps null → 404, so a soft-deleted org can't be
    /// re-deleted/moved). MUST be called inside the caller's tx BEFORE the blocked-if-employees
    /// check + the flip/re-parent so the count and the write see a consistent, exclusively-held
    /// row (the create-vs-delete TOCTOU residual is accepted+documented — GlobalAdmin, recoverable).</summary>
    public async Task<Organization?> LockActiveByIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string orgId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM organizations WHERE org_id = @orgId AND is_active = TRUE FOR UPDATE",
            conn, tx);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadOrg(reader) : null;
    }

    /// <summary>S98 — counts the ACTIVE users that block a soft-delete of <paramref name="org"/>.
    /// An ORGANISATION blocks if any active user's <c>primary_org_id</c> equals it; a MAO blocks
    /// if any active user lives anywhere beneath it (their Organisation's <c>materialized_path</c>
    /// is prefixed by the MAO's path). Returns the blocking count (0 → the delete is allowed).
    /// Runs on the caller's tx so the count is consistent with the FOR-UPDATE'd org row.</summary>
    public async Task<long> CountActiveEmployeesBlockingDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Organization org, CancellationToken ct = default)
    {
        if (string.Equals(org.OrgType, "MAO", StringComparison.Ordinal))
        {
            // MAO subtree: any active user whose home Organisation sits under this MAO's path.
            // EscapeLike guards a literal '%'/'_' in the (system-derived) path from widening.
            await using var cmd = new NpgsqlCommand(
                """
                SELECT COUNT(*)
                FROM users u
                JOIN organizations o ON o.org_id = u.primary_org_id
                WHERE u.is_active = TRUE
                  AND o.materialized_path LIKE @pathPrefix ESCAPE '\'
                """, conn, tx);
            cmd.Parameters.AddWithValue("pathPrefix", EscapeLike(org.MaterializedPath) + "%");
            return (long)(await cmd.ExecuteScalarAsync(ct))!;
        }

        // ORGANISATION: active users homed directly on this org.
        await using var orgCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE primary_org_id = @orgId AND is_active = TRUE",
            conn, tx);
        orgCmd.Parameters.AddWithValue("orgId", org.OrgId);
        return (long)(await orgCmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>S98 — flips the (FOR-UPDATE'd) org row to <c>is_active = FALSE</c> in the caller's
    /// tx. The GetByIdAsync/GetAllAsync reads already filter <c>is_active=TRUE</c>, so a deleted
    /// org disappears from the list + the create/transfer home guards (Step-0b BLOCKER B — the
    /// protection already exists, no new guard added).</summary>
    public async Task SoftDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string orgId, DateTime now, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE organizations SET is_active = FALSE, updated_at = @now WHERE org_id = @orgId",
            conn, tx);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>S98 — re-parents the (FOR-UPDATE'd) ORGANISATION row under <paramref name="newParent"/>
    /// in the caller's tx: sets <c>parent_org_id</c> + RECOMPUTES the moved row's OWN
    /// <c>materialized_path</c> = <c>newParent.materialized_path || orgId || '/'</c>. NO descendant
    /// cascade — Organisations are org-tree leaves; users key on org_id, not the path. The
    /// path rewrite is load-bearing: the tree-roster reads scope by <c>materialized_path LIKE</c>
    /// (ApprovalPeriodRepository), so an unrewritten path silently drops the org's employees.
    /// Returns the NEW materialized_path (for the event payload).</summary>
    public async Task<string> ReparentAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string orgId, Organization newParent, DateTime now, CancellationToken ct = default)
    {
        var newPath = $"{newParent.MaterializedPath}{orgId}/";
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE organizations
            SET parent_org_id = @newParent,
                materialized_path = @newPath,
                updated_at = @now
            WHERE org_id = @orgId
            """, conn, tx);
        cmd.Parameters.AddWithValue("newParent", newParent.OrgId);
        cmd.Parameters.AddWithValue("newPath", newPath);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await cmd.ExecuteNonQueryAsync(ct);
        return newPath;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  S98 / ADR-035 — aggregated tree-with-counts reads (set-based, NO N+1).
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>S98 — one set-based <c>GROUP BY primary_org_id</c> over ACTIVE users → the
    /// per-Organisation active-employee count. Self-managed connection (read-only). The endpoint
    /// assembles the forest: each Organisation = its own count; each MAO = Σ its Organisations.</summary>
    public async Task<IReadOnlyDictionary<string, long>> GetActiveEmployeeCountByOrgAsync(
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT primary_org_id, COUNT(*) AS cnt
            FROM users
            WHERE is_active = TRUE
            GROUP BY primary_org_id
            """, conn);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            counts[reader.GetString(0)] = reader.GetInt64(1);
        return counts;
    }

    /// <summary>Escapes LIKE metacharacters in a (system-derived) materialized_path so a literal
    /// '%' or '_' cannot widen the prefix into a wildcard (cross-MAO over-match). Mirrors the
    /// ApprovalPeriodRepository EscapeLike used by the tree-roster reads; the '\' is the ESCAPE.</summary>
    private static string EscapeLike(string raw)
        => raw.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static async Task<IReadOnlyList<Organization>> ReadOrgsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var orgs = new List<Organization>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            orgs.Add(ReadOrg(reader));
        return orgs;
    }

    private static Organization ReadOrg(NpgsqlDataReader reader) => new()
    {
        OrgId = reader.GetString(reader.GetOrdinal("org_id")),
        OrgName = reader.GetString(reader.GetOrdinal("org_name")),
        OrgType = reader.GetString(reader.GetOrdinal("org_type")),
        ParentOrgId = reader.IsDBNull(reader.GetOrdinal("parent_org_id")) ? null : reader.GetString(reader.GetOrdinal("parent_org_id")),
        MaterializedPath = reader.GetString(reader.GetOrdinal("materialized_path")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
