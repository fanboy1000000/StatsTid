using Npgsql;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

/// <summary>
/// S104 / ADR-038 D3/D8/D10 (Enhedsspor) — read-model + projection surface for the typed
/// structural <c>units</c> hierarchy and its designated leaders (<c>unit_leaders</c>). A unit is
/// STRUCTURE + reporting only — role-SCOPE stays anchored at the Organisation (the LOCKED D5
/// invariant): <c>parent_unit_id</c> / <c>unit_id</c> / <c>unit_leaders</c> enter NO scope path.
///
/// <para>
/// Modelled on the S100 hierarchical-enhed spine: a per-Organisation advisory lock
/// (<see cref="AcquireUnitOrgLockAsync"/>) + a recursive-CTE cycle guard
/// (<see cref="GuardNoUnitCycleAsync"/>) over <c>parent_unit_id</c>, taken on EVERY structural
/// mutator so concurrent moves/creates/deletes serialize. NON-temporal latest-wins projection: the
/// in-tx writers below are the canonical write path — the calling endpoint enqueues the matching
/// event in the SAME transaction (ADR-018 D3) so the projection commits/rolls-back atomically with
/// the event.
/// </para>
/// </summary>
public sealed class UnitRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public UnitRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Reads (self-managed connection)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Lists the ACTIVE (non-soft-deleted) units for ONE Organisation, ordered by
    /// lower(name). The endpoint floors org-scope (LocalHR) BEFORE calling this.</summary>
    public async Task<IReadOnlyList<UnitRow>> ListActiveByOrgAsync(
        string organisationId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT unit_id, organisation_id, parent_unit_id, type, name, version
            FROM units
            WHERE organisation_id = @org AND deleted_at IS NULL
            ORDER BY lower(name), unit_id
            """, conn);
        cmd.Parameters.AddWithValue("org", organisationId);

        var rows = new List<UnitRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new UnitRow(
                UnitId: reader.GetGuid(0),
                OrganisationId: reader.GetString(1),
                ParentUnitId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Type: reader.GetString(3),
                Name: reader.GetString(4),
                Version: reader.GetInt64(5)));
        }
        return rows;
    }

    /// <summary>Reads a single unit (active OR soft-deleted) by id, or <c>null</c>. Used by the
    /// rename/move/delete endpoints to resolve the owning Organisation (for the org-scope floor)
    /// + the current version (for the If-Match check).</summary>
    public async Task<UnitFullRow?> GetByIdAsync(string unitId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(unitId, out var id))
            return null;
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT unit_id, organisation_id, parent_unit_id, type, name, version, (deleted_at IS NOT NULL) AS is_deleted
            FROM units
            WHERE unit_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return ReadFullRow(reader);
    }

    /// <summary>In-tx re-read of a single unit (active OR soft-deleted) on the HELD connection.
    /// Distinguishes a 0-row optimistic-concurrency UPDATE: row absent or soft-deleted → 404;
    /// version mismatch → 412.</summary>
    public async Task<UnitFullRow?> GetByIdInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid unitId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT unit_id, organisation_id, parent_unit_id, type, name, version, (deleted_at IS NOT NULL) AS is_deleted
            FROM units
            WHERE unit_id = @id
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", unitId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return ReadFullRow(reader);
    }

    /// <summary>In-tx read of an ACTIVE candidate-parent unit on the HELD connection: returns its
    /// <c>(organisation_id, type)</c> iff the row exists AND is ACTIVE (<c>deleted_at IS NULL</c>),
    /// otherwise <c>null</c> (absent or soft-deleted). The create/move endpoints compare it against
    /// the child's Organisation (same-Organisation invariant) + rank (the partial-rank CHILD
    /// ordering) — run under the <c>unit-org-</c> lock so a concurrent parent-delete serializes.</summary>
    public async Task<UnitParentInfo?> GetActiveUnitInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid unitId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT organisation_id, type FROM units WHERE unit_id = @id AND deleted_at IS NULL",
            conn, tx);
        cmd.Parameters.AddWithValue("id", unitId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new UnitParentInfo(reader.GetString(0), reader.GetString(1));
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Concurrency spine — the per-Organisation advisory lock + the cycle CTE
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Effectively-unbounded safety ceiling for the unit-cycle descendant walk; the
    /// path-array visited-set guard is the real termination guarantee.</summary>
    private const int UnitCycleWalkMaxDepth = 10_000;

    /// <summary>
    /// S104 — takes a STABLE, per-Organisation advisory lock keyed on the unit's
    /// <paramref name="organisationId"/> (a unit tree is WHOLLY within one Organisation —
    /// <c>units.organisation_id</c> IMMUTABLE per row). xact-scoped (auto-released at
    /// COMMIT/ROLLBACK). Acquired FIRST — before the cycle CTE — on EVERY unit-tree mutator
    /// (create / move / delete-reparent / leader-designate / same-Org member move) so concurrent
    /// moves serialize: the cycle walk of the second move sees the first's committed parent edge.
    ///
    /// <para>A DISTINCT prefix (<c>unit-org-</c>) from the reporting <c>reporting-org-</c> key — the
    /// total lock order (ADR-038 D8) is all <c>reporting-org-</c> (id-sorted) → all <c>unit-org-</c>
    /// (id-sorted) → any row <c>FOR UPDATE</c>; the two advisory domains compose without an AB/BA
    /// cycle.</para>
    /// </summary>
    public static async Task AcquireUnitOrgLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string organisationId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('unit-org-' || @organisationId))", conn, tx);
        cmd.Parameters.AddWithValue("organisationId", organisationId);
        await cmd.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// S104 — the unit-tree cycle guard. REJECTS (via <see cref="UnitCycleException"/>) a move whose
    /// chosen <paramref name="newParentUnitId"/> is the <paramref name="unitId"/> itself OR any
    /// active descendant of the unit. Run inside the caller's transaction AFTER
    /// <see cref="AcquireUnitOrgLockAsync"/>, on the HELD connection. A recursive CTE over
    /// <c>units.parent_unit_id</c> (filtered <c>deleted_at IS NULL</c>) with a path-array visited-set
    /// guard + a depth backstop (termination even on a pre-existing loop). The walk goes DOWN from the
    /// moved unit; if <paramref name="newParentUnitId"/> is reached, the move is a self-into-descendant
    /// cycle.
    /// </summary>
    /// <exception cref="UnitCycleException">If the new parent is the unit itself or a descendant.</exception>
    public async Task GuardNoUnitCycleAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid unitId, Guid newParentUnitId, CancellationToken ct = default)
    {
        if (unitId == newParentUnitId)
            throw new UnitCycleException(unitId, newParentUnitId,
                $"Cannot move unit '{unitId}' under itself.");

        await using var cmd = new NpgsqlCommand(
            """
            WITH RECURSIVE descendants AS (
                SELECT u.unit_id, 1 AS depth, ARRAY[u.parent_unit_id, u.unit_id] AS path
                FROM units u
                WHERE u.parent_unit_id = @unitId
                  AND u.deleted_at IS NULL
                UNION ALL
                SELECT u.unit_id, d.depth + 1, d.path || u.unit_id
                FROM units u
                INNER JOIN descendants d ON u.parent_unit_id = d.unit_id
                WHERE u.deleted_at IS NULL
                  AND d.depth < @maxDepth
                  AND NOT (u.unit_id = ANY(d.path))
            )
            SELECT 1 FROM descendants WHERE unit_id = @newParentUnitId LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("unitId", unitId);
        cmd.Parameters.AddWithValue("newParentUnitId", newParentUnitId);
        cmd.Parameters.AddWithValue("maxDepth", UnitCycleWalkMaxDepth);

        var hit = await cmd.ExecuteScalarAsync(ct);
        if (hit is not null)
            throw new UnitCycleException(unitId, newParentUnitId,
                $"Cannot move unit '{unitId}' under '{newParentUnitId}': the target is a descendant of " +
                "the unit, which would create a cycle.");
    }

    // ──────────────────────────────────────────────────────────────────────
    //  In-tx structural writers (ADR-018 D3 — caller enqueues the event in the same tx)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>UnitCreated projection — INSERT a fresh active unit at version 1. A <c>23505</c> on
    /// <c>idx_units_active_name</c> surfaces as a PostgresException → the endpoint maps it to 409.</summary>
    public async Task ApplyUnitCreatedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, UnitCreated @event, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, organisation_id, parent_unit_id, type, name, deleted_at, version, created_at)
            VALUES (@id, @org, @parent, @type, @name, NULL, 1, NOW())
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", @event.UnitId);
        cmd.Parameters.AddWithValue("org", @event.OrganisationId);
        cmd.Parameters.AddWithValue("parent", (object?)@event.ParentUnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("type", @event.Type);
        cmd.Parameters.AddWithValue("name", @event.Name);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>UnitRenamed projection — UPDATE name + bump version, IN-UPDATE optimistic concurrency
    /// (the <c>version = @expectedVersion AND deleted_at IS NULL</c> predicate runs INSIDE the tx).
    /// Returns the affected-row count (0 → version mismatch OR absent/soft-deleted → caller re-reads).</summary>
    public async Task<int> ApplyUnitRenamedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid unitId, string newName, long expectedVersion,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE units
            SET name = @name, version = version + 1
            WHERE unit_id = @id AND version = @expectedVersion AND deleted_at IS NULL
            """, conn, tx);
        cmd.Parameters.AddWithValue("name", newName);
        cmd.Parameters.AddWithValue("id", unitId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>UnitMoved projection — UPDATE <c>parent_unit_id</c> + bump version, IN-UPDATE
    /// optimistic concurrency. <paramref name="newParentUnitId"/> <c>null</c> = make the unit
    /// top-level. Returns the affected-row count.</summary>
    public async Task<int> ApplyUnitMovedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid unitId, Guid? newParentUnitId, long expectedVersion, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE units
            SET parent_unit_id = @parent, version = version + 1
            WHERE unit_id = @id AND version = @expectedVersion AND deleted_at IS NULL
            """, conn, tx);
        cmd.Parameters.AddWithValue("parent", (object?)newParentUnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", unitId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>UnitDeleted projection — SOFT delete (set <c>deleted_at</c>, bump version), IN-UPDATE
    /// optimistic concurrency. Returns the affected-row count (0 → version mismatch OR already
    /// deleted/absent → caller re-reads).</summary>
    public async Task<int> ApplyUnitDeletedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid unitId, long expectedVersion,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE units
            SET deleted_at = NOW(), version = version + 1
            WHERE unit_id = @id AND version = @expectedVersion AND deleted_at IS NULL
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", unitId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Re-parent-on-delete writer — lifts EVERY active direct child of
    /// <paramref name="deletedUnitId"/> UP to <paramref name="grandparentUnitId"/> (the deleted
    /// unit's own parent; <c>null</c> = the children become top-level) + bumps each child's version.
    /// Returns the moved children's ids (the caller emits a per-child <see cref="UnitMoved"/> in the
    /// SAME tx — P3, NOT a silent SQL update). Type-safe by construction under the partial-rank CHILD
    /// ordering (a child's rank is strictly deeper than the deleted unit's, which is strictly deeper
    /// than the grandparent's → the child stays rank-deeper than the grandparent).</summary>
    public async Task<IReadOnlyList<Guid>> ReparentChildrenOnDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid deletedUnitId, Guid? grandparentUnitId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE units
            SET parent_unit_id = @grandparent, version = version + 1
            WHERE parent_unit_id = @deletedId AND deleted_at IS NULL
            RETURNING unit_id
            """, conn, tx);
        cmd.Parameters.AddWithValue("grandparent", (object?)grandparentUnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deletedId", deletedUnitId);

        var moved = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            moved.Add(reader.GetGuid(0));
        return moved;
    }

    /// <summary>Re-home-on-delete writer — lifts EVERY DIRECT member of
    /// <paramref name="deletedUnitId"/> UP to <paramref name="parentUnitId"/> (the deleted unit's own
    /// parent; <c>null</c> = the members home directly at the Organisation) + bumps each member's
    /// version. <c>primary_org_id</c> is UNCHANGED (the parent unit is in the same Organisation — a
    /// no-op). Returns the re-homed members' ids (the caller emits a per-member
    /// <see cref="UserUnitChanged"/> in the SAME tx — P3).
    /// <para>NO <c>is_active</c> filter (S104 Step-7a fix): an INACTIVE member must re-home too, else a
    /// soft-deleted user keeps <c>unit_id</c> = the deleted unit and could be reactivated (the admin PUT
    /// preserves <c>unit_id</c>) into a deleted unit with no repair event. Re-home ALL members so no
    /// user — active or not — ever points at a soft-deleted unit.</para></summary>
    public async Task<IReadOnlyList<string>> RehomeMembersOnDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid deletedUnitId, Guid? parentUnitId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users
            SET unit_id = @parent, version = version + 1, updated_at = NOW()
            WHERE unit_id = @deletedId
            RETURNING user_id
            """, conn, tx);
        cmd.Parameters.AddWithValue("parent", (object?)parentUnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deletedId", deletedUnitId);

        var members = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            members.Add(reader.GetString(0));
        return members;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Leader designation (ADR-038 D3) + membership writers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Reads + FOR UPDATE-locks an ACTIVE user's <c>(unit_id, primary_org_id, version)</c>
    /// on the HELD connection, or <c>null</c> when the user does not exist or is inactive. Used by the
    /// leader-designate endpoint (the member-invariant check) + the same-Org member-move endpoint (the
    /// If-Match gate). Mirrors the AdminEndpoints users-PUT FOR-UPDATE lock-order discipline.</summary>
    public async Task<UserUnitInfo?> GetUserUnitForUpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT unit_id, primary_org_id, version FROM users WHERE user_id = @u AND is_active = TRUE FOR UPDATE",
            conn, tx);
        cmd.Parameters.AddWithValue("u", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new UserUnitInfo(
            UnitId: reader.IsDBNull(0) ? null : reader.GetGuid(0),
            PrimaryOrgId: reader.GetString(1),
            Version: reader.GetInt64(2));
    }

    /// <summary>Reads an ACTIVE user's current <c>unit_id</c> on the HELD connection (no lock — the
    /// caller has already FOR-UPDATE'd the row, e.g. the cross-Org transfer's users-row pin), or
    /// <c>null</c> when absent/inactive/unit-less.</summary>
    public async Task<Guid?> GetUserUnitIdInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT unit_id FROM users WHERE user_id = @u AND is_active = TRUE",
            conn, tx);
        cmd.Parameters.AddWithValue("u", userId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (Guid)result;
    }

    /// <summary>UnitLeaderDesignated projection — INSERT a <c>unit_leaders(unit_id, user_id)</c> row.
    /// Idempotent: <c>ON CONFLICT DO NOTHING</c> returns 0 when the designation already exists (the
    /// caller skips the event). The caller MUST have verified the member-invariant (the user's
    /// <c>unit_id == unit_id</c>) under the <c>unit-org-</c> lock BEFORE calling.</summary>
    public async Task<int> DesignateLeaderAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid unitId, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO unit_leaders (unit_id, user_id) VALUES (@unit, @user) ON CONFLICT DO NOTHING",
            conn, tx);
        cmd.Parameters.AddWithValue("unit", unitId);
        cmd.Parameters.AddWithValue("user", userId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>UnitLeaderRemoved projection — DELETE the <c>unit_leaders(unit_id, user_id)</c> row.
    /// Returns the affected-row count (0 → no such designation → caller skips the event).</summary>
    public async Task<int> RemoveLeaderAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid unitId, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM unit_leaders WHERE unit_id = @unit AND user_id = @user",
            conn, tx);
        cmd.Parameters.AddWithValue("unit", unitId);
        cmd.Parameters.AddWithValue("user", userId);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Clears ALL <c>unit_leaders</c> rows for a UNIT (the unit-delete cascade): the
    /// designations vanish with the unit. Returns the removed leaders' user_ids so the caller emits a
    /// per-row <see cref="UnitLeaderRemoved"/> in the SAME tx (P3, NOT a silent SQL delete).</summary>
    public async Task<IReadOnlyList<string>> RemoveAllLeadershipForUnitAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid unitId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM unit_leaders WHERE unit_id = @unit RETURNING user_id",
            conn, tx);
        cmd.Parameters.AddWithValue("unit", unitId);

        var users = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            users.Add(reader.GetString(0));
        return users;
    }

    /// <summary>Removes ALL of a user's <c>unit_leaders</c> rows (the member-invariant re-sync, D3):
    /// when a person's <c>unit_id</c> changes, any leader designation they held becomes stale (a
    /// non-member cannot lead). Because the member-invariant bounds a person to leading only their
    /// OWN unit, in practice this removes at most the rows for the unit they are leaving. Returns the
    /// removed (unit_id) set so the caller emits a per-row <see cref="UnitLeaderRemoved"/> in the SAME
    /// tx.</summary>
    public async Task<IReadOnlyList<Guid>> RemoveAllLeadershipForUserAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM unit_leaders WHERE user_id = @user RETURNING unit_id",
            conn, tx);
        cmd.Parameters.AddWithValue("user", userId);

        var units = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            units.Add(reader.GetGuid(0));
        return units;
    }

    /// <summary>UserUnitChanged projection — UPDATE <c>users.unit_id</c> + the derived
    /// <c>primary_org_id</c> + bump version, IN-UPDATE optimistic concurrency (the
    /// <c>version = @expectedVersion AND is_active</c> predicate runs INSIDE the tx so a stale
    /// If-Match matches 0 rows). Returns the affected-row count. <paramref name="newUnitId"/>
    /// <c>null</c> = the person homes directly at the Organisation.</summary>
    public async Task<int> ApplyUserUnitChangedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string userId, Guid? newUnitId, string newPrimaryOrgId, long expectedVersion,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users
            SET unit_id = @unit, primary_org_id = @org, version = version + 1, updated_at = NOW()
            WHERE user_id = @u AND is_active = TRUE AND version = @expectedVersion
            """, conn, tx);
        cmd.Parameters.AddWithValue("unit", (object?)newUnitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("org", newPrimaryOrgId);
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static UnitFullRow ReadFullRow(NpgsqlDataReader reader) => new(
        UnitId: reader.GetGuid(0),
        OrganisationId: reader.GetString(1),
        ParentUnitId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
        Type: reader.GetString(3),
        Name: reader.GetString(4),
        Version: reader.GetInt64(5),
        IsDeleted: reader.GetBoolean(6));
}

/// <summary>An active unit list row (for the managed-list GET). <c>ParentUnitId</c> is carried FLAT
/// (<c>null</c> = top-level under the Organisation); level is DERIVED (depth) and is implied by the
/// typed <c>Type</c>.</summary>
public sealed record UnitRow(
    Guid UnitId, string OrganisationId, Guid? ParentUnitId, string Type, string Name, long Version);

/// <summary>A full unit row incl. soft-delete state (for rename/move/delete resolution).</summary>
public sealed record UnitFullRow(
    Guid UnitId, string OrganisationId, Guid? ParentUnitId, string Type, string Name, long Version, bool IsDeleted);

/// <summary>The <c>(organisation_id, type)</c> of an ACTIVE candidate-parent unit (for the
/// same-Organisation + partial-rank CHILD-ordering checks).</summary>
public sealed record UnitParentInfo(string OrganisationId, string Type);

/// <summary>An ACTIVE user's structural-home snapshot (for the leader member-invariant + the
/// same-Org member-move If-Match gate).</summary>
public sealed record UserUnitInfo(Guid? UnitId, string PrimaryOrgId, long Version);

/// <summary>
/// S104 / ADR-038 D8 — thrown by <see cref="UnitRepository.GuardNoUnitCycleAsync"/> when a move would
/// create a cycle in the unit tree: the chosen new parent is the unit itself, or one of the unit's
/// descendants. The endpoint maps this to <c>422 Unprocessable Entity</c>. This guards tree SHAPE
/// only, NOT any authority decision (units carry NO scope — the LOCKED D5 boundary).
/// </summary>
public sealed class UnitCycleException : Exception
{
    public Guid UnitId { get; }
    public Guid NewParentUnitId { get; }

    public UnitCycleException(Guid unitId, Guid newParentUnitId, string message)
        : base(message)
    {
        UnitId = unitId;
        NewParentUnitId = newParentUnitId;
    }
}
