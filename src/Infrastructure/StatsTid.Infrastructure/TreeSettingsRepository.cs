using System.Data;
using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Repository for <see cref="TreeSettings"/> rows in <c>reporting_line_tree_settings</c>.
///
/// Follows the same pattern as <see cref="ReportingLineRepository"/>: read methods open
/// their own connection via the injected <see cref="DbConnectionFactory"/>. Write methods
/// have two flavors:
/// <list type="bullet">
/// <item><description>Self-contained: owns its own connection and transaction.</description></item>
/// <item><description>In-transaction sibling: reuses a caller-supplied connection + transaction
/// so the caller can extend the same PostgreSQL transaction across outbox + audit-row writes
/// (ADR-018 D3 transactional outbox contract).</description></item>
/// </list>
/// </summary>
public sealed class TreeSettingsRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public TreeSettingsRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Read methods
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full <see cref="TreeSettings"/> for the given tree root,
    /// or <c>null</c> if no row exists (default PREFERRED behavior).
    /// </summary>
    public async Task<TreeSettings?> GetAsync(string treeRootOrgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_line_tree_settings
            WHERE tree_root_org_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", treeRootOrgId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// Shortcut: returns the enforcement mode for the tree root, or "PREFERRED"
    /// if no row exists. For use by the approval endpoint where the full settings
    /// object is not needed.
    /// </summary>
    public async Task<string> GetEnforcementModeAsync(string treeRootOrgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT enforcement_mode FROM reporting_line_tree_settings
            WHERE tree_root_org_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", treeRootOrgId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string mode ? mode : "PREFERRED";
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Write methods — self-contained overload
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Upserts the tree settings. Self-contained overload: opens its own
    /// connection and transaction.
    /// </summary>
    /// <returns>The persisted <see cref="TreeSettings"/>.</returns>
    /// <exception cref="OptimisticConcurrencyException">If <paramref name="expectedVersion"/>
    /// does not match the current row's version.</exception>
    public async Task<TreeSettings> UpsertAsync(
        string treeRootOrgId,
        string enforcementMode,
        long? expectedVersion,
        string actorId,
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            var result = await UpsertAsync(conn, tx, treeRootOrgId, enforcementMode, expectedVersion, actorId, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="UpsertAsync(string, string, long?, string, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> and <paramref name="tx"/> so the
    /// caller can extend the same PostgreSQL transaction across outbox + audit-row writes.
    /// The caller is responsible for committing or rolling back the transaction.
    /// </summary>
    /// <returns>The persisted <see cref="TreeSettings"/>.</returns>
    /// <exception cref="OptimisticConcurrencyException">If <paramref name="expectedVersion"/>
    /// does not match the current row's version.</exception>
    public async Task<TreeSettings> UpsertAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string treeRootOrgId,
        string enforcementMode,
        long? expectedVersion,
        string actorId,
        CancellationToken ct = default)
    {
        if (expectedVersion is null or 0)
        {
            // First creation — INSERT with version=1.
            await using var insertCmd = new NpgsqlCommand(
                """
                INSERT INTO reporting_line_tree_settings
                    (tree_root_org_id, enforcement_mode, version, updated_by, updated_at)
                VALUES
                    (@id, @mode, 1, @actor, NOW())
                RETURNING *
                """, conn, tx);
            insertCmd.Parameters.AddWithValue("id", treeRootOrgId);
            insertCmd.Parameters.AddWithValue("mode", enforcementMode);
            insertCmd.Parameters.AddWithValue("actor", actorId);

            try
            {
                await using var reader = await insertCmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    throw new InvalidOperationException(
                        $"INSERT into reporting_line_tree_settings for tree_root_org_id='{treeRootOrgId}' " +
                        "returned no rows.");
                }
                return MapReader(reader);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                // Another caller concurrently inserted for the same tree root.
                throw new OptimisticConcurrencyException(
                    $"A tree settings row for tree_root_org_id='{treeRootOrgId}' was created concurrently; " +
                    "refresh and retry with the current version.",
                    expectedVersion: expectedVersion,
                    actualVersion: null,
                    innerException: ex);
            }
        }
        else
        {
            // Update — version must match.
            await using var updateCmd = new NpgsqlCommand(
                """
                UPDATE reporting_line_tree_settings
                SET enforcement_mode = @mode,
                    version = version + 1,
                    updated_by = @actor,
                    updated_at = NOW()
                WHERE tree_root_org_id = @id
                  AND version = @expected
                RETURNING *
                """, conn, tx);
            updateCmd.Parameters.AddWithValue("id", treeRootOrgId);
            updateCmd.Parameters.AddWithValue("mode", enforcementMode);
            updateCmd.Parameters.AddWithValue("actor", actorId);
            updateCmd.Parameters.AddWithValue("expected", expectedVersion.Value);

            await using var reader = await updateCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new OptimisticConcurrencyException(
                    $"Tree settings for tree_root_org_id='{treeRootOrgId}' version mismatch: " +
                    $"expected {expectedVersion.Value}, but the row was modified or does not exist.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            return MapReader(reader);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Population gate (TASK-5004)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates that the reporting tree for the given tree root is fully populated:
    /// every active user whose org resolves to the tree root has an active PRIMARY
    /// reporting line, except for the tree root person (who has no reporting line by
    /// design — they are the departementschef/styrelsesdirektør).
    /// </summary>
    /// <returns>
    /// A tuple where <c>IsPopulated</c> is <c>true</c> if there are no unassigned employees,
    /// and <c>UnassignedEmployeeIds</c> lists the user IDs that lack an active PRIMARY line.
    /// </returns>
    public async Task<(bool IsPopulated, List<string> UnassignedEmployeeIds)> ValidateTreePopulatedAsync(
        string treeRootOrgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            WITH tree_users AS (
                SELECT u.user_id
                FROM users u
                JOIN organizations o ON o.org_id = u.primary_org_id
                WHERE u.is_active = TRUE
                  AND o.materialized_path LIKE (
                      SELECT materialized_path FROM organizations WHERE org_id = @treeRootOrgId
                  ) || '%'
            ),
            tree_roots AS (
                -- Managers who appear as manager_id but NOT as employee_id in the tree's reporting lines
                SELECT DISTINCT rl.manager_id AS user_id
                FROM reporting_lines rl
                WHERE rl.tree_root_org_id = @treeRootOrgId
                  AND rl.effective_to IS NULL
                  AND rl.relationship = 'PRIMARY'
                  AND rl.manager_id NOT IN (
                      SELECT rl2.employee_id FROM reporting_lines rl2
                      WHERE rl2.tree_root_org_id = @treeRootOrgId
                        AND rl2.effective_to IS NULL
                        AND rl2.relationship = 'PRIMARY'
                  )
            ),
            has_any_lines AS (
                -- Check if the tree has ANY active reporting lines at all
                SELECT EXISTS (
                    SELECT 1 FROM reporting_lines rl
                    WHERE rl.tree_root_org_id = @treeRootOrgId
                      AND rl.effective_to IS NULL
                ) AS has_lines
            )
            SELECT tu.user_id
            FROM tree_users tu
            WHERE tu.user_id NOT IN (
                SELECT rl.employee_id FROM reporting_lines rl
                WHERE rl.tree_root_org_id = @treeRootOrgId
                  AND rl.relationship = 'PRIMARY'
                  AND rl.effective_to IS NULL
            )
            AND tu.user_id NOT IN (SELECT user_id FROM tree_roots)
            -- If the tree has NO lines at all (e.g., single-person root-only tree like STY03),
            -- then every user is an implicit root — exclude them all (tree is trivially populated).
            AND (SELECT has_lines FROM has_any_lines) = TRUE
            """, conn);
        cmd.Parameters.AddWithValue("treeRootOrgId", treeRootOrgId);

        var unassigned = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            unassigned.Add(reader.GetString(0));
        }

        return (unassigned.Count == 0, unassigned);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────────────────

    private static TreeSettings MapReader(NpgsqlDataReader reader) => new()
    {
        TreeRootOrgId = reader.GetString(reader.GetOrdinal("tree_root_org_id")),
        EnforcementMode = reader.GetString(reader.GetOrdinal("enforcement_mode")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        UpdatedBy = reader.GetString(reader.GetOrdinal("updated_by")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
    };
}
