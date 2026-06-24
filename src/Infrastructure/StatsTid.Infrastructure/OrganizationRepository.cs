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
    /// cascade — Organisations are org-tree leaves; users/enheder key on org_id, not the path. The
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

    /// <summary>S98 — one set-based query over ACTIVE enheder LEFT JOIN user_enheder → per
    /// (Organisation, enhed) the active-enhed identity + its tagged-user count. Soft-deleted
    /// enheder (deleted_at IS NOT NULL) are excluded; the LEFT JOIN yields 0 for an untagged
    /// enhed. Self-managed connection (read-only). Grouped by Organisation in C# by the endpoint.
    /// <para>S98 Step-7a FIX 2 (P1/P9 active-only count) — the tag count must EXCLUDE inactive
    /// users: tags aren't cleared on deactivation (only on transfer), so a terminated-but-tagged
    /// user would inflate <c>taggedUserCount</c> and disagree with the active-only
    /// <c>employeeCount</c> on the same node. The <c>users u … AND u.is_active = TRUE</c> predicate
    /// rides INSIDE the LEFT JOIN's ON clause (NOT a WHERE) so a 0-tag active enhed is still listed
    /// (COUNT counts non-null <c>u.user_id</c> → 0 when no active tagged user).</para></summary>
    public async Task<IReadOnlyList<EnhedCountRow>> GetActiveEnhederWithTagCountsAsync(
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT e.organisation_id, e.enhed_id, e.parent_enhed_id, e.name, COUNT(u.user_id) AS tagged
            FROM enheder e
            LEFT JOIN user_enheder ue ON ue.enhed_id = e.enhed_id
            LEFT JOIN users u ON u.user_id = ue.user_id AND u.is_active = TRUE
            WHERE e.deleted_at IS NULL
            GROUP BY e.organisation_id, e.enhed_id, e.parent_enhed_id, e.name
            ORDER BY e.organisation_id, lower(e.name), e.enhed_id
            """, conn);
        var rows = new List<EnhedCountRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new EnhedCountRow(
                OrganisationId: reader.GetString(0),
                EnhedId: reader.GetGuid(1),
                ParentEnhedId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Name: reader.GetString(3),
                TaggedUserCount: reader.GetInt64(4)));
        }
        return rows;
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

/// <summary>S98 — one row of the aggregated tree's enhed-with-tag-count read: an active enhed,
/// its owning Organisation, and how many users are tagged into it. S100: <c>ParentEnhedId</c>
/// (<c>null</c> = a root) lets the GET /tree assembly NEST the per-Organisation enhed sub-tree +
/// derive the <c>level</c> = depth. PURE DISPLAY metadata — ZERO authority.</summary>
public sealed record EnhedCountRow(
    string OrganisationId, Guid EnhedId, Guid? ParentEnhedId, string Name, long TaggedUserCount);
