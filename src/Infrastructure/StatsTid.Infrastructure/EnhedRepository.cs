using Npgsql;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

/// <summary>
/// S97 / ADR-035 — read-model + projection surface for the structured Enhed metadata
/// (<c>enheder</c>) and the multi-tag membership link (<c>user_enheder</c>). An Enhed is
/// PURE DISPLAY metadata: ZERO authority/scope/approval/payroll meaning (the Organisation
/// is the only authority unit; this repository is ABSENT from every authority path —
/// <c>OrgScopeValidator</c> / <c>RoleScope.CoversOrg</c> / <c>DesignatedApproverAuthorizer</c>).
///
/// <para>
/// NON-temporal latest-wins projection (model after <c>WorkTimeProjectionRepository</c> /
/// ADR-028 D1, NOT the ADR-022 temporal profile). The in-tx writers
/// (<see cref="ApplyEnhedCreatedAsync"/> / <see cref="ApplyEnhedRenamedAsync"/> /
/// <see cref="ApplyEnhedDeletedAsync"/> / <see cref="ApplyUserEnhederChangedAsync"/>) are
/// the canonical write path: the calling endpoint enqueues the matching event in the SAME
/// transaction (ADR-018 D3) so the projection commits/rolls-back atomically with the event
/// and read-your-write holds without waiting for the publisher drain.
/// </para>
/// </summary>
public sealed class EnhedRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public EnhedRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Reads (self-managed connection)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Lists the ACTIVE (non-soft-deleted) enheder for ONE Organisation, ordered
    /// by lower(name). The endpoint floors org-scope (LocalHR) BEFORE calling this.</summary>
    public async Task<IReadOnlyList<EnhedRow>> ListActiveByOrgAsync(
        string organisationId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT enhed_id, organisation_id, parent_enhed_id, name, version
            FROM enheder
            WHERE organisation_id = @org AND deleted_at IS NULL
            ORDER BY lower(name), enhed_id
            """, conn);
        cmd.Parameters.AddWithValue("org", organisationId);

        var rows = new List<EnhedRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new EnhedRow(
                EnhedId: reader.GetGuid(0),
                OrganisationId: reader.GetString(1),
                ParentEnhedId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
                Name: reader.GetString(3),
                Version: reader.GetInt64(4)));
        }
        return rows;
    }

    /// <summary>Reads a single enhed (active OR soft-deleted) by id, or <c>null</c>. Used by
    /// the rename/delete endpoints to resolve the owning Organisation (for the org-scope
    /// floor) + the current version (for the If-Match check).</summary>
    public async Task<EnhedFullRow?> GetByIdAsync(string enhedId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT enhed_id, organisation_id, parent_enhed_id, name, version, (deleted_at IS NOT NULL) AS is_deleted
            FROM enheder
            WHERE enhed_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.Parse(enhedId));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new EnhedFullRow(
            EnhedId: reader.GetGuid(0),
            OrganisationId: reader.GetString(1),
            ParentEnhedId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
            Name: reader.GetString(3),
            Version: reader.GetInt64(4),
            IsDeleted: reader.GetBoolean(5));
    }

    /// <summary>In-tx re-read of a single enhed (active OR soft-deleted) on the HELD connection.
    /// Used by the rename/delete endpoints to distinguish a 0-row optimistic-concurrency UPDATE:
    /// row absent or <c>deleted_at IS NOT NULL</c> → 404; version mismatch → 412. Runs on the
    /// caller's tx so the read is consistent with the same-tx UPDATE.</summary>
    public async Task<EnhedFullRow?> GetByIdInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid enhedId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT enhed_id, organisation_id, parent_enhed_id, name, version, (deleted_at IS NOT NULL) AS is_deleted
            FROM enheder
            WHERE enhed_id = @id
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", enhedId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new EnhedFullRow(
            EnhedId: reader.GetGuid(0),
            OrganisationId: reader.GetString(1),
            ParentEnhedId: reader.IsDBNull(2) ? null : reader.GetGuid(2),
            Name: reader.GetString(3),
            Version: reader.GetInt64(4),
            IsDeleted: reader.GetBoolean(5));
    }

    /// <summary>Reads a user's ACTIVE enhed-id set (for projecting the current tag set into
    /// the EditPersonDrawer / display). Soft-deleted enheder are filtered out.</summary>
    public async Task<IReadOnlyList<Guid>> GetUserActiveEnhedIdsAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT ue.enhed_id
            FROM user_enheder ue
            JOIN enheder e ON e.enhed_id = ue.enhed_id
            WHERE ue.user_id = @u AND e.deleted_at IS NULL
            ORDER BY ue.enhed_id
            """, conn);
        cmd.Parameters.AddWithValue("u", userId);

        var ids = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  In-tx projection writers (ADR-018 D3 — caller enqueues the event in the same tx)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>EnhedCreated projection — INSERT a fresh active enhed at version 1. The
    /// caller generates <paramref name="event"/>.EnhedId so the outbox event body carries it.
    /// A <c>23505</c> on <c>idx_enheder_active_name</c> (active-name dup) surfaces as a
    /// PostgresException — the endpoint maps it to 409. S100: <c>parent_enhed_id</c> comes
    /// from <paramref name="event"/>.ParentEnhedId (<c>null</c> = a root; validated active +
    /// same-Organisation by the caller IN-TX under the per-Organisation advisory lock).</summary>
    public async Task ApplyEnhedCreatedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EnhedCreated @event, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO enheder (enhed_id, organisation_id, parent_enhed_id, name, deleted_at, version, created_at)
            VALUES (@id, @org, @parent, @name, NULL, 1, NOW())
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", @event.EnhedId);
        cmd.Parameters.AddWithValue("org", @event.OrganisationId);
        cmd.Parameters.AddWithValue("parent", (object?)@event.ParentEnhedId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("name", @event.Name);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>EnhedRenamed projection — UPDATE name + bump version on the active row,
    /// IN-UPDATE optimistic-concurrency (BLOCKER 2): the <c>version = @expectedVersion</c>
    /// predicate runs INSIDE the write tx so two concurrent <c>If-Match:"N"</c> renames can NOT
    /// both commit (the second matches 0 rows). Returns the affected-row count: <c>1</c> → the
    /// write landed (the caller emits the event + bumps the ETag); <c>0</c> → version mismatch OR
    /// the row was concurrently soft-deleted/absent (the caller re-reads to map 412 vs 404 and
    /// MUST NOT emit the event). A <c>23505</c> (rename-to-existing-active-name) still surfaces as
    /// a PostgresException → the endpoint maps it to 409.</summary>
    public async Task<int> ApplyEnhedRenamedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EnhedRenamed @event, long expectedVersion,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE enheder
            SET name = @name, version = version + 1
            WHERE enhed_id = @id AND version = @expectedVersion AND deleted_at IS NULL
            """, conn, tx);
        cmd.Parameters.AddWithValue("name", @event.Name);
        cmd.Parameters.AddWithValue("id", @event.EnhedId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>EnhedDeleted projection — SOFT delete (set deleted_at, bump version). NO
    /// fan-out untag write — memberships are projection-FILTERED at read time. IN-UPDATE
    /// optimistic-concurrency (BLOCKER 2): the <c>version = @expectedVersion</c> predicate runs
    /// INSIDE the write tx (a stale If-Match or an already-deleted row matches 0 rows). Returns
    /// the affected-row count: <c>1</c> → the soft-delete landed (the caller emits the event);
    /// <c>0</c> → version mismatch OR already-deleted/absent (the caller re-reads to map 412 vs
    /// 404 and MUST NOT emit the event).</summary>
    public async Task<int> ApplyEnhedDeletedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EnhedDeleted @event, long expectedVersion,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE enheder
            SET deleted_at = NOW(), version = version + 1
            WHERE enhed_id = @id AND version = @expectedVersion AND deleted_at IS NULL
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", @event.EnhedId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  S100 (ADR-036 amendment) — the hierarchical-Enhed concurrency spine
    //  (the per-Organisation advisory lock + the cycle CTE under it + the
    //  re-parent writer). PURE DISPLAY metadata — these mutate parent_enhed_id
    //  ONLY; ZERO authority/scope/approval meaning.
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Effectively-unbounded safety ceiling for the enhed-cycle descendant walk; the
    /// path-array visited-set guard is the real termination guarantee (mirrors
    /// <c>ReportingLineRepository.CycleWalkMaxDepth</c>).</summary>
    private const int EnhedCycleWalkMaxDepth = 10_000;

    /// <summary>
    /// S100 — takes a STABLE, per-Organisation advisory lock keyed on the enhed's
    /// <paramref name="organisationId"/> (the enhed tree is WHOLLY within one Organisation — the
    /// S95 "advisory domain = the Organisation" pattern). xact-scoped (auto-released at
    /// COMMIT/ROLLBACK; no manual unlock). Acquired FIRST — before the cycle CTE — on EVERY
    /// enhed-tree mutator (create-child / move / delete-reparent) so concurrent moves serialize:
    /// the cycle walk of the second move sees the first's committed parent edge (or blocks until
    /// it commits/rolls back), closing the phantom-cycle gap where two stale snapshots each pass.
    ///
    /// <para>A DISTINCT prefix (<c>enhed-org-</c>) from the S95 reporting <c>reporting-org-</c>
    /// key — enhed mutations never touch reporting_lines and vice-versa, so the two advisory
    /// domains can never alias into false contention or a cross-domain deadlock.</para>
    /// </summary>
    public static async Task AcquireEnhedOrgLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string organisationId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('enhed-org-' || @organisationId))", conn, tx);
        cmd.Parameters.AddWithValue("organisationId", organisationId);
        await cmd.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// S100 — the enhed-tree cycle guard. REJECTS (via <see cref="EnhedCycleException"/>) a move
    /// whose chosen <paramref name="newParentEnhedId"/> is the <paramref name="enhedId"/> itself
    /// OR any active descendant of the enhed (which would detach a sub-tree into a cycle). Run
    /// inside the caller's transaction AFTER <see cref="AcquireEnhedOrgLockAsync"/>, on the HELD
    /// connection (so the descendant walk sees the committed parent edges).
    ///
    /// <para>A NEW recursive CTE over <c>enheder.parent_enhed_id</c> (filtered
    /// <c>deleted_at IS NULL</c>) — NOT a reuse of <c>ReportingLineRepository.GuardNoCycleAsync</c>
    /// (that walks <c>reporting_lines</c> edges, structurally unusable here), but mirroring its
    /// discipline: a path-array visited-set guard terminates even on a pre-existing loop, with
    /// <see cref="EnhedCycleWalkMaxDepth"/> as the effectively-unbounded backstop. The walk goes
    /// DOWN (<c>parent_enhed_id = @enhedId</c>) from the moved enhed; if
    /// <paramref name="newParentEnhedId"/> is reached, the move is a self-into-descendant cycle.</para>
    /// </summary>
    /// <exception cref="EnhedCycleException">If the new parent is the enhed itself or a descendant.</exception>
    public async Task GuardNoEnhedCycleAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid enhedId, Guid newParentEnhedId, CancellationToken ct = default)
    {
        // Self-cycle: an enhed cannot be its own parent.
        if (enhedId == newParentEnhedId)
            throw new EnhedCycleException(enhedId, newParentEnhedId,
                $"Cannot move enhed '{enhedId}' under itself.");

        // Descendant-cycle: if the chosen parent is somewhere BELOW the enhed in the tree, the
        // re-parent would close a loop. Walk downward (parent_enhed_id → enhed_id) from the enhed
        // and reject if we reach the new parent. The path-array visited-set guard + the depth
        // backstop guarantee termination even if the data already contains a loop.
        await using var cmd = new NpgsqlCommand(
            """
            WITH RECURSIVE descendants AS (
                SELECT e.enhed_id, 1 AS depth, ARRAY[e.parent_enhed_id, e.enhed_id] AS path
                FROM enheder e
                WHERE e.parent_enhed_id = @enhedId
                  AND e.deleted_at IS NULL
                UNION ALL
                SELECT e.enhed_id, d.depth + 1, d.path || e.enhed_id
                FROM enheder e
                INNER JOIN descendants d ON e.parent_enhed_id = d.enhed_id
                WHERE e.deleted_at IS NULL
                  AND d.depth < @maxDepth
                  AND NOT (e.enhed_id = ANY(d.path))   -- guard against a pre-existing loop
            )
            SELECT 1 FROM descendants WHERE enhed_id = @newParentEnhedId LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("enhedId", enhedId);
        cmd.Parameters.AddWithValue("newParentEnhedId", newParentEnhedId);
        cmd.Parameters.AddWithValue("maxDepth", EnhedCycleWalkMaxDepth);

        var hit = await cmd.ExecuteScalarAsync(ct);
        if (hit is not null)
            throw new EnhedCycleException(enhedId, newParentEnhedId,
                $"Cannot move enhed '{enhedId}' under '{newParentEnhedId}': the target is a " +
                "descendant of the enhed, which would create a cycle.");
    }

    /// <summary>In-tx re-read of an enhed's <c>(organisation_id, is_active-as-non-deleted)</c> for
    /// VALIDATING a candidate parent on the HELD connection: returns the parent's
    /// <c>organisation_id</c> iff the row exists AND is ACTIVE (<c>deleted_at IS NULL</c>),
    /// otherwise <c>null</c> (absent or soft-deleted). The create/move endpoints compare it
    /// against the child's Organisation (same-Organisation invariant; cross-org parent rejected)
    /// — run under the <c>enhed-org-</c> lock so a concurrent parent-delete serializes.</summary>
    public async Task<string?> GetActiveEnhedOrgInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid enhedId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT organisation_id FROM enheder WHERE enhed_id = @id AND deleted_at IS NULL",
            conn, tx);
        cmd.Parameters.AddWithValue("id", enhedId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    /// <summary>EnhedMoved projection — UPDATE <c>parent_enhed_id</c> + bump <c>version</c> on the
    /// active row, IN-UPDATE optimistic-concurrency: the <c>version = @expectedVersion AND
    /// deleted_at IS NULL</c> predicate runs INSIDE the write tx so a stale If-Match or a
    /// concurrently-soft-deleted row matches 0 rows (the caller re-reads to map 412 vs 404 and
    /// emits NOTHING). Returns the affected-row count. <paramref name="newParentEnhedId"/>
    /// <c>null</c> = make the enhed a root.</summary>
    public async Task<int> ApplyEnhedMovedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid enhedId, Guid? newParentEnhedId, long expectedVersion, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE enheder
            SET parent_enhed_id = @parent, version = version + 1
            WHERE enhed_id = @id AND version = @expectedVersion AND deleted_at IS NULL
            """, conn, tx);
        cmd.Parameters.AddWithValue("parent", (object?)newParentEnhedId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", enhedId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Re-parent-on-delete writer — lifts EVERY active direct child of
    /// <paramref name="deletedEnhedId"/> UP to <paramref name="grandparentEnhedId"/> (the deleted
    /// enhed's own parent; <c>null</c> = the children become roots) + bumps each child's
    /// <c>version</c>. Returns the moved children's ids (the caller emits a per-child
    /// <see cref="EnhedMoved"/> in the SAME tx — P3, NOT a silent SQL update). Run under the
    /// <c>enhed-org-</c> lock, in the same tx as the <c>EnhedDeleted</c>. A LEAF (0 children)
    /// returns an empty list → the caller emits ONLY <c>EnhedDeleted</c>.</summary>
    public async Task<IReadOnlyList<Guid>> ReparentChildrenOnDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid deletedEnhedId, Guid? grandparentEnhedId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE enheder
            SET parent_enhed_id = @grandparent, version = version + 1
            WHERE parent_enhed_id = @deletedId AND deleted_at IS NULL
            RETURNING enhed_id
            """, conn, tx);
        cmd.Parameters.AddWithValue("grandparent", (object?)grandparentEnhedId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deletedId", deletedEnhedId);

        var movedChildren = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            movedChildren.Add(reader.GetGuid(0));
        return movedChildren;
    }

    /// <summary>UserEnhederChanged projection — delete-all-then-insert the FULL set for the
    /// user (idempotent overwrite). An EMPTY set clears the user's tags (the transfer-clears
    /// path). The caller MUST have validated each enhed_id against the user's locked
    /// primary_org's active enheder BEFORE calling (set-tags TOCTOU guard); this writer is a
    /// pure projection apply.</summary>
    public async Task ApplyUserEnhederChangedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, UserEnhederChanged @event, CancellationToken ct = default)
    {
        await using (var del = new NpgsqlCommand(
            "DELETE FROM user_enheder WHERE user_id = @u", conn, tx))
        {
            del.Parameters.AddWithValue("u", @event.UserId);
            await del.ExecuteNonQueryAsync(ct);
        }

        foreach (var enhedId in @event.EnhedIds)
        {
            await using var ins = new NpgsqlCommand(
                "INSERT INTO user_enheder (user_id, enhed_id) VALUES (@u, @e)", conn, tx);
            ins.Parameters.AddWithValue("u", @event.UserId);
            ins.Parameters.AddWithValue("e", enhedId);
            await ins.ExecuteNonQueryAsync(ct);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Set-tags TOCTOU guard (WARNING C) — used by the set-user-tags endpoint
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Locks the user row (<c>SELECT primary_org_id ... FOR UPDATE</c>) and returns
    /// the user's current primary_org_id, or <c>null</c> when the user does not exist (or is
    /// inactive). MUST be called inside the caller's tx BEFORE validating + writing tags so a
    /// concurrent transfer serializes either before (the new org's enheder fail validation)
    /// or after (the transfer's clear wins). Mirrors the AdminEndpoints users-PUT FOR-UPDATE
    /// lock-order discipline.</summary>
    public async Task<string?> LockUserPrimaryOrgAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string userId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT primary_org_id FROM users WHERE user_id = @u AND is_active = TRUE FOR UPDATE",
            conn, tx);
        cmd.Parameters.AddWithValue("u", userId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    /// <summary>Returns the subset of <paramref name="enhedIds"/> that are VALID tags for the
    /// (locked) <paramref name="organisationId"/> — i.e. exist, belong to that Organisation,
    /// and are NOT soft-deleted. The endpoint compares this against the requested set: any
    /// requested id missing from the result is a dead/foreign enhed → 400. Runs inside the
    /// caller's tx on the held connection (after the FOR-UPDATE lock).</summary>
    public async Task<IReadOnlyList<Guid>> FilterValidActiveEnhedIdsForOrgAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string organisationId, IReadOnlyCollection<Guid> enhedIds, CancellationToken ct = default)
    {
        if (enhedIds.Count == 0)
            return Array.Empty<Guid>();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT enhed_id
            FROM enheder
            WHERE organisation_id = @org
              AND deleted_at IS NULL
              AND enhed_id = ANY(@ids)
            """, conn, tx);
        cmd.Parameters.AddWithValue("org", organisationId);
        cmd.Parameters.AddWithValue("ids", enhedIds.ToArray());

        var valid = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            valid.Add(reader.GetGuid(0));
        return valid;
    }
}

/// <summary>An active enhed list row (for the managed-list GET + the tag picker). S100:
/// <c>ParentEnhedId</c> is carried FLAT (<c>null</c> = a root) — the list reads stay flat (the
/// tag picker is set-membership); only the GET /tree assembly + the management panels NEST.</summary>
public sealed record EnhedRow(
    Guid EnhedId, string OrganisationId, Guid? ParentEnhedId, string Name, long Version);

/// <summary>A full enhed row incl. soft-delete state (for rename/delete/move resolution). S100:
/// <c>ParentEnhedId</c> is on the in-tx read path — the move needs the OLD parent (for the
/// <c>EnhedMoved</c> event), the delete-reparent reads the deleted enhed's parent.</summary>
public sealed record EnhedFullRow(
    Guid EnhedId, string OrganisationId, Guid? ParentEnhedId, string Name, long Version, bool IsDeleted);

/// <summary>
/// S100 (ADR-036 amendment) — thrown by <see cref="EnhedRepository.GuardNoEnhedCycleAsync"/> when
/// a move would create a cycle in the enhed tree: the chosen new parent is the enhed itself, or
/// one of the enhed's descendants. The endpoint maps this to <c>422 Unprocessable Entity</c> (the
/// move is well-formed but conflicts with the acyclic-tree invariant). PURE DISPLAY metadata —
/// this guards tree shape only, NOT any authority decision.
/// </summary>
public sealed class EnhedCycleException : Exception
{
    public Guid EnhedId { get; }
    public Guid NewParentEnhedId { get; }

    public EnhedCycleException(Guid enhedId, Guid newParentEnhedId, string message)
        : base(message)
    {
        EnhedId = enhedId;
        NewParentEnhedId = newParentEnhedId;
    }
}
