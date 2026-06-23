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
            SELECT enhed_id, organisation_id, name, version
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
                Name: reader.GetString(2),
                Version: reader.GetInt64(3)));
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
            SELECT enhed_id, organisation_id, name, version, (deleted_at IS NOT NULL) AS is_deleted
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
            Name: reader.GetString(2),
            Version: reader.GetInt64(3),
            IsDeleted: reader.GetBoolean(4));
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
            SELECT enhed_id, organisation_id, name, version, (deleted_at IS NOT NULL) AS is_deleted
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
            Name: reader.GetString(2),
            Version: reader.GetInt64(3),
            IsDeleted: reader.GetBoolean(4));
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
    /// PostgresException — the endpoint maps it to 409.</summary>
    public async Task ApplyEnhedCreatedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EnhedCreated @event, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO enheder (enhed_id, organisation_id, name, deleted_at, version, created_at)
            VALUES (@id, @org, @name, NULL, 1, NOW())
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", @event.EnhedId);
        cmd.Parameters.AddWithValue("org", @event.OrganisationId);
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

/// <summary>An active enhed list row (for the managed-list GET + the tag picker).</summary>
public sealed record EnhedRow(Guid EnhedId, string OrganisationId, string Name, long Version);

/// <summary>A full enhed row incl. soft-delete state (for rename/delete resolution).</summary>
public sealed record EnhedFullRow(
    Guid EnhedId, string OrganisationId, string Name, long Version, bool IsDeleted);
