using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// S72 / TASK-7201 — DB-facing surface for the Skema row-preference model (SPRINT-72 pinned
/// rule R4): the <c>user_skema_preferences</c> configured-state container and the
/// <c>user_absence_selections</c> per-user visible-absence-row set, plus the full-replacement
/// write the <c>PUT /api/skema/{employeeId}/row-preferences</c> endpoint drives.
///
/// <para>
/// <b>View preferences, not domain state.</b> Rows in these tables are plain un-evented
/// writes per the <see cref="ProjectRepository"/> selection precedent — NO outbox event, NO
/// audit projection, NO ADR-019 version (R4). The container's presence is the R4 state
/// switch: container absent → the month GET serves today's fallback (all org projects / all
/// filtered absence types); container present → the selection rows are authoritative EVEN
/// WHEN EMPTY (zero visible rows is a legal, deliberate state).
/// </para>
///
/// <para>
/// <b>Table ownership split (R13).</b> <see cref="ProjectRepository"/> keeps owning
/// <c>user_project_selections</c>' existing single-row methods (the R14-aligned legacy
/// add/remove); THIS repository owns the container, the absence selections, and the
/// one-transaction full-replacement write that spans all three tables.
/// </para>
/// </summary>
public sealed class SkemaRowPreferenceRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public SkemaRowPreferenceRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>One absence-row selection: the type plus its per-user order.</summary>
    public sealed record AbsenceSelection(string AbsenceType, int SortOrder);

    /// <summary>
    /// R4 state switch: does the employee have a <c>user_skema_preferences</c> container row?
    /// Absent → fallback semantics; present → selections authoritative even when empty.
    /// </summary>
    public async Task<bool> ContainerExistsAsync(string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM user_skema_preferences WHERE employee_id = @employeeId", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    /// <summary>
    /// The employee's absence-row selections in the R4 deterministic read order:
    /// <c>ORDER BY sort_order, absence_type</c> (the same tiebreak family as the project
    /// read's <c>ORDER BY sort_order, project_code</c>).
    /// </summary>
    public async Task<IReadOnlyList<AbsenceSelection>> GetAbsenceSelectionsAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT absence_type, sort_order
            FROM user_absence_selections
            WHERE employee_id = @employeeId
            ORDER BY sort_order, absence_type
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);

        var selections = new List<AbsenceSelection>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            selections.Add(new AbsenceSelection(reader.GetString(0), reader.GetInt32(1)));
        return selections;
    }

    /// <summary>
    /// The R4 full-replacement write, ONE transaction:
    /// <list type="number">
    ///   <item>acquire the per-employee advisory lock FIRST (S72 / Step-5a B4 — the
    ///     established ADR-032 D4 <c>hashtext('employee-' || id)</c> key via
    ///     <see cref="ProjectRepository.AcquireEmployeeLockAsync"/>, held to commit), so
    ///     this replacement and the R14-aligned legacy add/remove serialize per employee
    ///     and can never interleave between each other's read-derive-write windows
    ///     (gap/duplicate <c>sort_order</c> corruption);</item>
    ///   <item>upsert the container (<c>ON CONFLICT DO NOTHING</c> — <c>initialized_at</c> is
    ///     stamped by its DEFAULT on the FIRST write only and preserved on every later
    ///     write);</item>
    ///   <item>DELETE-and-INSERT the employee's <c>user_project_selections</c> rows with the
    ///     caller-supplied DENSE <c>sort_order</c> (the endpoint assigns 0..n-1 in submitted
    ///     order);</item>
    ///   <item>DELETE-and-INSERT the employee's <c>user_absence_selections</c> rows
    ///     likewise.</item>
    /// </list>
    /// Plain un-evented rows (R4): no outbox enqueue, no audit projection — deliberately NOT
    /// the ADR-018 atomic-outbox shape, mirroring the pre-S72 <c>user_project_selections</c>
    /// precedent. The caller validates every id/type against the current catalog BEFORE
    /// calling (write-side validation lives at the endpoint, where the catalog chain is).
    /// </summary>
    public async Task ReplaceAsync(
        string employeeId,
        IReadOnlyList<(Guid ProjectId, int SortOrder)> projects,
        IReadOnlyList<(string AbsenceType, int SortOrder)> absenceTypes,
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // (0) S72 / Step-5a B4 — the per-employee advisory lock, FIRST statement in the
            // tx (the same key every preference mutation acquires; see the helper's doc).
            await ProjectRepository.AcquireEmployeeLockAsync(conn, tx, employeeId, ct);

            // (1) Container — first write initializes (DEFAULT NOW() stamps initialized_at);
            // every later write is a no-op so the configured-since timestamp is stable.
            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO user_skema_preferences (employee_id)
                VALUES (@employeeId)
                ON CONFLICT (employee_id) DO NOTHING
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("employeeId", employeeId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // (2) Project selections — full replacement, dense sort_order as supplied.
            await using (var cmd = new NpgsqlCommand(
                "DELETE FROM user_project_selections WHERE employee_id = @employeeId", conn, tx))
            {
                cmd.Parameters.AddWithValue("employeeId", employeeId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            foreach (var (projectId, sortOrder) in projects)
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO user_project_selections (employee_id, project_id, sort_order)
                    VALUES (@employeeId, @projectId, @sortOrder)
                    """, conn, tx);
                cmd.Parameters.AddWithValue("employeeId", employeeId);
                cmd.Parameters.AddWithValue("projectId", projectId);
                cmd.Parameters.AddWithValue("sortOrder", sortOrder);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            // (3) Absence selections — full replacement, dense sort_order as supplied.
            await using (var cmd = new NpgsqlCommand(
                "DELETE FROM user_absence_selections WHERE employee_id = @employeeId", conn, tx))
            {
                cmd.Parameters.AddWithValue("employeeId", employeeId);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            foreach (var (absenceType, sortOrder) in absenceTypes)
            {
                await using var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO user_absence_selections (employee_id, absence_type, sort_order)
                    VALUES (@employeeId, @absenceType, @sortOrder)
                    """, conn, tx);
                cmd.Parameters.AddWithValue("employeeId", employeeId);
                cmd.Parameters.AddWithValue("absenceType", absenceType);
                cmd.Parameters.AddWithValue("sortOrder", sortOrder);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
