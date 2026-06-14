using System.Data;
using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S74 / ADR-027 Phase 5 (SPRINT-74 R2/R3/R4, TASK-7401). Repository for the
/// <c>manager_vikar</c> table — the approver-owned vikar (stand-in approver) that is
/// the go-forward storage for self-service delegation, REPLACING the per-report
/// <c>SELF_DELEGATION</c> ACTING fan-out in <c>reporting_lines</c>.
///
/// <para>
/// Mirrors <see cref="ReportingLineRepository"/>'s idiom: read methods open their own
/// connection via the injected <see cref="DbConnectionFactory"/>; writes have a
/// self-contained overload (owns its connection + transaction) and an in-transaction
/// sibling overload (<c>(conn, tx)</c>) so the calling endpoint can extend the same
/// PostgreSQL transaction across outbox + audit writes (ADR-018 D3 transactional outbox).
/// </para>
///
/// <para>
/// At most one ACTIVE vikar per <c>absent_approver_id</c> is enforced at the schema
/// level by the partial-unique-index <c>uq_manager_vikar_active</c> WHERE
/// <c>effective_to IS NULL</c> — by-construction, not by application check (the S68-B1
/// lesson). A concurrent second active create collides on the index INSERT (23505),
/// which the create method translates to <see cref="OptimisticConcurrencyException"/>.
/// </para>
///
/// <para>
/// This repository is a pure CRUD facade — it does NOT enqueue outbox events or write
/// audit rows; the calling endpoint / service coordinates those alongside the mutation.
/// </para>
/// </summary>
public sealed class ManagerVikarRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ManagerVikarRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Read methods (no transaction needed)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the active vikar row owned by <paramref name="absentApproverId"/> that
    /// covers <paramref name="asOf"/>, or <c>null</c> if none. "Active + covering" means
    /// <c>effective_to IS NULL AND until_date &gt;= asOf</c> — the <c>until_date</c> is the
    /// INCLUSIVE last-covered day ("til og med", R4a). The partial-unique invariant
    /// guarantees at most one open row per approver, so this returns zero or one.
    /// </summary>
    public async Task<ManagerVikar?> GetActiveByApproverAsync(
        string absentApproverId, DateOnly asOf, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await GetActiveByApproverAsync(conn, absentApproverId, asOf, tx: null, ct);
    }

    /// <summary>
    /// Connection-reusing overload of
    /// <see cref="GetActiveByApproverAsync(string, DateOnly, CancellationToken)"/> — used by
    /// the resolver, which already owns a read connection (and by the in-tx callers via the
    /// transaction's connection). Does NOT take FOR UPDATE; this is a read.
    /// </summary>
    public async Task<ManagerVikar?> GetActiveByApproverAsync(
        NpgsqlConnection conn, string absentApproverId, DateOnly asOf,
        NpgsqlTransaction? tx = null, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM manager_vikar
            WHERE absent_approver_id = @absentApproverId
              AND effective_to IS NULL
              AND until_date >= @asOf
            """, conn, tx);
        cmd.Parameters.AddWithValue("absentApproverId", absentApproverId);
        cmd.Parameters.AddWithValue("asOf", asOf);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// Returns the active (open) vikar row owned by <paramref name="absentApproverId"/>
    /// REGARDLESS of date coverage (<c>effective_to IS NULL</c> only) — used by the POST
    /// re-delegate path to detect / supersede an already-active delegation, and by GET to
    /// surface the current status.
    /// </summary>
    public async Task<ManagerVikar?> GetActiveByApproverAnyDateAsync(
        string absentApproverId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM manager_vikar
            WHERE absent_approver_id = @absentApproverId
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("absentApproverId", absentApproverId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// S76 / TASK-7601 fix-forward (Step-5a c1 B3) — IN-TX, <c>FOR UPDATE</c> read of the active
    /// vikar row owned by <paramref name="absentApproverId"/> (<c>effective_to IS NULL</c>). Used
    /// by the admin-revoke DELETE so the row that is AUTHORIZED against (its persisted
    /// <c>tree_root_org_id</c>) is the EXACT row that is then CLOSED — the <c>FOR UPDATE</c> pin
    /// (under the tree advisory lock) makes the authorize→close pair atomic: a concurrent
    /// close/recreate cannot swap the active row between the authorization read and the UPDATE.
    /// Returns <c>null</c> when no active row exists (→ 404).
    /// </summary>
    public async Task<ManagerVikar?> GetActiveByApproverForUpdateInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string absentApproverId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM manager_vikar
            WHERE absent_approver_id = @absentApproverId
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("absentApproverId", absentApproverId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// Reverse lookup: returns all active vikar rows that name <paramref name="vikarUserId"/>
    /// as the stand-in (<c>vikar_user_id = @id AND effective_to IS NULL</c>). Served by the
    /// <c>idx_manager_vikar_vikar</c> partial index. Used by the R10 delete-closure
    /// (TASK-7403) when the stand-in user is removed from the tree.
    /// </summary>
    public async Task<IReadOnlyList<ManagerVikar>> GetActiveByVikarUserAsync(
        string vikarUserId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM manager_vikar
            WHERE vikar_user_id = @vikarUserId
              AND effective_to IS NULL
            ORDER BY absent_approver_id
            """, conn);
        cmd.Parameters.AddWithValue("vikarUserId", vikarUserId);
        var rows = new List<ManagerVikar>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(MapReader(reader));
        return rows;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Write methods
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new active vikar row. In-transaction overload (ADR-018 D3): the caller
    /// supplies <paramref name="conn"/> + <paramref name="tx"/> so the INSERT shares the
    /// same atomic transaction as the outbox + audit writes. A concurrent second active
    /// create for the same approver collides on <c>uq_manager_vikar_active</c> (23505),
    /// translated to <see cref="OptimisticConcurrencyException"/>.
    /// </summary>
    public async Task<ManagerVikar> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, ManagerVikar newVikar, CancellationToken ct = default)
    {
        var newId = newVikar.VikarId == Guid.Empty ? Guid.NewGuid() : newVikar.VikarId;
        var createdAt = newVikar.CreatedAt == default ? DateTime.UtcNow : newVikar.CreatedAt;
        var version = newVikar.Version <= 0 ? 1 : newVikar.Version;

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO manager_vikar (
                vikar_id, absent_approver_id, vikar_user_id, until_date, reason,
                tree_root_org_id, version, created_by, created_at, effective_to)
            VALUES (
                @vikarId, @absentApproverId, @vikarUserId, @untilDate, @reason,
                @treeRootOrgId, @version, @createdBy, @createdAt, NULL)
            """, conn, tx);
        cmd.Parameters.AddWithValue("vikarId", newId);
        cmd.Parameters.AddWithValue("absentApproverId", newVikar.AbsentApproverId);
        cmd.Parameters.AddWithValue("vikarUserId", newVikar.VikarUserId);
        cmd.Parameters.AddWithValue("untilDate", newVikar.UntilDate);
        cmd.Parameters.AddWithValue("reason", newVikar.Reason);
        cmd.Parameters.AddWithValue("treeRootOrgId", newVikar.TreeRootOrgId);
        cmd.Parameters.AddWithValue("version", version);
        cmd.Parameters.AddWithValue("createdBy", newVikar.CreatedBy);
        cmd.Parameters.AddWithValue("createdAt", createdAt);

        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (
            ex.SqlState == "23505" && ex.ConstraintName == "uq_manager_vikar_active")
        {
            throw new OptimisticConcurrencyException(
                $"An active vikar already exists for approver '{newVikar.AbsentApproverId}'; " +
                "revoke it first or supersede.",
                expectedVersion: null,
                actualVersion: null,
                innerException: ex);
        }

        return new ManagerVikar
        {
            VikarId = newId,
            AbsentApproverId = newVikar.AbsentApproverId,
            VikarUserId = newVikar.VikarUserId,
            UntilDate = newVikar.UntilDate,
            Reason = newVikar.Reason,
            TreeRootOrgId = newVikar.TreeRootOrgId,
            Version = version,
            CreatedBy = newVikar.CreatedBy,
            CreatedAt = createdAt,
            EffectiveTo = null,
        };
    }

    /// <summary>
    /// Closes (ends) a specific active vikar row at <paramref name="effectiveTo"/> with a
    /// version bump, returning the closed row, or <c>null</c> if it was already closed by a
    /// concurrent writer. In-transaction overload (ADR-018 D3). Idempotent under the
    /// <c>effective_to IS NULL</c> guard.
    /// </summary>
    public async Task<ManagerVikar?> CloseAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid vikarId, DateOnly effectiveTo, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE manager_vikar
            SET effective_to = @effectiveTo, version = version + 1
            WHERE vikar_id = @vikarId
              AND effective_to IS NULL
            RETURNING *
            """, conn, tx);
        cmd.Parameters.AddWithValue("vikarId", vikarId);
        cmd.Parameters.AddWithValue("effectiveTo", effectiveTo);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// Closes the active vikar row owned by <paramref name="absentApproverId"/> (if any) at
    /// <paramref name="effectiveTo"/> with a version bump, returning the closed row, or
    /// <c>null</c> if no active row existed. In-transaction overload (ADR-018 D3). Used by
    /// the DELETE /delegate revoke path, which keys on the actor (= absent approver) rather
    /// than a known vikar_id.
    /// </summary>
    public async Task<ManagerVikar?> CloseByApproverAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string absentApproverId, DateOnly effectiveTo, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE manager_vikar
            SET effective_to = @effectiveTo, version = version + 1
            WHERE absent_approver_id = @absentApproverId
              AND effective_to IS NULL
            RETURNING *
            """, conn, tx);
        cmd.Parameters.AddWithValue("absentApproverId", absentApproverId);
        cmd.Parameters.AddWithValue("effectiveTo", effectiveTo);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Mapper
    // ──────────────────────────────────────────────────────────────────────

    private static ManagerVikar MapReader(NpgsqlDataReader reader) => new()
    {
        VikarId = reader.GetGuid(reader.GetOrdinal("vikar_id")),
        AbsentApproverId = reader.GetString(reader.GetOrdinal("absent_approver_id")),
        VikarUserId = reader.GetString(reader.GetOrdinal("vikar_user_id")),
        UntilDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("until_date"))),
        Reason = reader.GetString(reader.GetOrdinal("reason")),
        TreeRootOrgId = reader.GetString(reader.GetOrdinal("tree_root_org_id")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
            ? null
            : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_to"))),
    };
}
