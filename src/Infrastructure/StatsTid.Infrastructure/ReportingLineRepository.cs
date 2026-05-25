using System.Data;
using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Repository for <see cref="ReportingLine"/> rows in <c>reporting_lines</c>.
///
/// Per ADR-027 D1 / ADR-017 D1 pattern: lifecycle is <c>effective_to</c>-only (no
/// <c>is_active</c>). At most one open-ended line per (employee_id, relationship)
/// is enforced at the schema level by the partial-unique-indexes
/// <c>uq_reporting_line_active_primary</c> and <c>uq_reporting_line_active_acting</c>
/// WHERE effective_to IS NULL. Write operations run inside a single
/// <see cref="IsolationLevel.RepeatableRead"/> transaction with <c>SELECT ... FOR UPDATE</c>
/// on the current open row to gate concurrent writers.
///
/// Pattern follows <see cref="LocalAgreementProfileRepository"/>: read methods open their own
/// connection via the injected <see cref="DbConnectionFactory"/>. Writes have two flavors:
/// <list type="bullet">
/// <item><description>Self-contained: owns its own connection and transaction.</description></item>
/// <item><description>In-transaction sibling: reuses a caller-supplied connection + transaction
/// so the caller can extend the same PostgreSQL transaction across outbox + audit-row writes
/// (ADR-018 D3 transactional outbox contract).</description></item>
/// </list>
///
/// This repository is a pure CRUD facade. It does NOT enqueue outbox events or write audit
/// rows; the calling endpoint is responsible for coordinating outbox + audit writes alongside
/// the reporting-line mutation.
/// </summary>
public sealed class ReportingLineRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ReportingLineRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Read methods (no transaction needed)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns both active PRIMARY and ACTING reporting lines for the employee,
    /// ordered by relationship (ACTING before PRIMARY alphabetically).
    /// </summary>
    public async Task<IReadOnlyList<ReportingLine>> GetActiveByEmployeeAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE employee_id = @employeeId
              AND effective_to IS NULL
            ORDER BY relationship
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await ReadLinesAsync(cmd, ct);
    }

    /// <summary>
    /// Returns the single active line of the given relationship type for the employee,
    /// or <c>null</c> if no active line exists. The partial-unique-index invariant
    /// guarantees zero or one match.
    /// </summary>
    public async Task<ReportingLine?> GetActiveByEmployeeAndRelationshipAsync(
        string employeeId, string relationship, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE employee_id = @employeeId
              AND relationship = @relationship
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("relationship", relationship);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// Returns all active lines where the given user is the manager,
    /// ordered by employee_id.
    /// </summary>
    public async Task<IReadOnlyList<ReportingLine>> GetDirectReportsAsync(
        string managerId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE manager_id = @managerId
              AND effective_to IS NULL
            ORDER BY employee_id
            """, conn);
        cmd.Parameters.AddWithValue("managerId", managerId);
        return await ReadLinesAsync(cmd, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="GetDirectReportsAsync(string, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the read
    /// participates in the same atomic transaction (e.g. when emitting
    /// <c>ReportingLineManagerDeactivated</c> events during user deactivation).
    /// </summary>
    public async Task<IReadOnlyList<ReportingLine>> GetDirectReportsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string managerId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE manager_id = @managerId
              AND effective_to IS NULL
            ORDER BY employee_id
            """, conn, tx);
        cmd.Parameters.AddWithValue("managerId", managerId);
        return await ReadLinesAsync(cmd, ct);
    }

    /// <summary>
    /// Returns all active lines in the given reporting tree,
    /// ordered by manager_id then employee_id.
    /// </summary>
    public async Task<IReadOnlyList<ReportingLine>> GetTreeAsync(
        string treeRootOrgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE tree_root_org_id = @treeRootOrgId
              AND effective_to IS NULL
            ORDER BY manager_id, employee_id
            """, conn);
        cmd.Parameters.AddWithValue("treeRootOrgId", treeRootOrgId);
        return await ReadLinesAsync(cmd, ct);
    }

    /// <summary>
    /// Returns all lines (active + closed) for the employee,
    /// ordered by effective_from descending (most recent first).
    /// </summary>
    public async Task<IReadOnlyList<ReportingLine>> GetHistoryAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE employee_id = @employeeId
            ORDER BY effective_from DESC
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await ReadLinesAsync(cmd, ct);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Write methods — self-contained overloads
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns a reporting line for the employee. If an active line of the same
    /// relationship type exists, it is superseded (closed at the new line's
    /// <c>effective_from</c>). Self-contained overload: opens its own connection
    /// and transaction.
    /// </summary>
    /// <returns>The persisted <see cref="ReportingLine"/> with generated UUID and version.</returns>
    /// <exception cref="OptimisticConcurrencyException">If the precondition encoded in
    /// <paramref name="expectedCurrentVersion"/> does not match the row currently holding the
    /// active slot.</exception>
    public async Task<ReportingLine> AssignAsync(
        long? expectedCurrentVersion, ReportingLine newLine, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            var result = await AssignAsync(conn, tx, expectedCurrentVersion, newLine, ct);
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
    /// In-transaction sibling overload of <see cref="AssignAsync(long?, ReportingLine, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> and <paramref name="tx"/> so the
    /// caller can extend the same PostgreSQL transaction across outbox + audit-row writes.
    /// The caller is responsible for committing or rolling back the transaction.
    /// </summary>
    public async Task<ReportingLine> AssignAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long? expectedCurrentVersion,
        ReportingLine newLine,
        CancellationToken ct = default)
    {
        // 1. Lock the currently-active row (if any) for this employee+relationship.
        var current = await AcquireLockAsync(conn, tx, newLine.EmployeeId, newLine.Relationship, ct);

        // 2. Validate optimistic-concurrency precondition.
        ValidatePrecondition(current, expectedCurrentVersion);

        // 3. Reject backdated supersessions (Codex S48 W1).
        //    Same-day replacement (effectiveFrom == current.EffectiveFrom) is allowed —
        //    a manager can be replaced the same day they were assigned.
        if (current is not null && newLine.EffectiveFrom < current.EffectiveFrom)
        {
            throw new InvalidOperationException(
                $"Cannot supersede reporting line: new effectiveFrom ({newLine.EffectiveFrom:yyyy-MM-dd}) " +
                $"must not be before the current line's effectiveFrom ({current.EffectiveFrom:yyyy-MM-dd}).");
        }

        // 4. If active line exists, supersede it by closing at newLine.EffectiveFrom.
        long nextVersion = 1;
        if (current is not null)
        {
            await CloseLineAsync(conn, tx, current.ReportingLineId, newLine.EffectiveFrom, ct);
            nextVersion = current.Version + 1; // Monotonic per-employee slot version (Codex S48 B1).
        }

        // 5. Insert the new line at the next monotonic version.
        var newId = newLine.ReportingLineId == Guid.Empty ? Guid.NewGuid() : newLine.ReportingLineId;
        var createdAt = newLine.CreatedAt == default ? DateTime.UtcNow : newLine.CreatedAt;

        try
        {
            await InsertLineAsync(conn, tx, newLine, newId, version: nextVersion, createdAt, ct);
        }
        catch (PostgresException ex) when (
            ex.SqlState == "23505" &&
            (ex.ConstraintName == "uq_reporting_line_active_primary" ||
             ex.ConstraintName == "uq_reporting_line_active_acting"))
        {
            // Two concurrent first-assignment requests can both pass ValidatePrecondition
            // (both see null) and collide on the partial-unique-index INSERT. Translate to
            // OptimisticConcurrencyException so the endpoint returns 412.
            throw new OptimisticConcurrencyException(
                "Another reporting line was created concurrently for the same " +
                $"(employee_id, relationship={newLine.Relationship}) pair; refresh and retry.",
                expectedVersion: expectedCurrentVersion,
                actualVersion: null,
                innerException: ex);
        }

        return new ReportingLine
        {
            ReportingLineId = newId,
            EmployeeId = newLine.EmployeeId,
            ManagerId = newLine.ManagerId,
            TreeRootOrgId = newLine.TreeRootOrgId,
            Relationship = newLine.Relationship,
            EffectiveFrom = newLine.EffectiveFrom,
            EffectiveTo = null,
            Source = newLine.Source,
            Version = nextVersion,
            ScheduledExpiry = newLine.ScheduledExpiry,
            CreatedBy = newLine.CreatedBy,
            CreatedAt = createdAt,
        };
    }

    /// <summary>
    /// Removes (closes) the active reporting line for the employee+relationship.
    /// Self-contained overload: opens its own connection and transaction.
    /// </summary>
    /// <returns>The closed <see cref="ReportingLine"/>.</returns>
    /// <exception cref="OptimisticConcurrencyException">If the precondition check fails.</exception>
    public async Task<ReportingLine> RemoveAsync(
        long expectedCurrentVersion, string employeeId, string relationship, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            var result = await RemoveAsync(conn, tx, expectedCurrentVersion, employeeId, relationship, ct);
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
    /// In-transaction sibling overload of <see cref="RemoveAsync(long, string, string, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> and <paramref name="tx"/>.
    /// </summary>
    public async Task<ReportingLine> RemoveAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long expectedCurrentVersion,
        string employeeId,
        string relationship,
        CancellationToken ct = default)
    {
        // 1. Lock the currently-active row.
        var current = await AcquireLockAsync(conn, tx, employeeId, relationship, ct);

        // 2. Validate: must have an active line, and version must match.
        ValidatePrecondition(current, expectedCurrentVersion);

        if (current is null)
        {
            // Unreachable after ValidatePrecondition with non-null expectedCurrentVersion,
            // but satisfies the compiler's null analysis.
            throw new InvalidOperationException(
                $"No active reporting line for employee_id={employeeId}, relationship={relationship}.");
        }

        // 3. Close it at CURRENT_DATE with version bump.
        return await CloseAndReturnLineAsync(conn, tx, current.ReportingLineId, ct);
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Helper / tree-resolution methods
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up <c>organizations.parent_org_id</c> from <paramref name="primaryOrgId"/>
    /// until finding an organization with <c>org_type IN ('MINISTRY', 'STYRELSE')</c>.
    /// Returns the <c>org_id</c> of the tree root.
    /// </summary>
    /// <exception cref="InvalidOperationException">If no MINISTRY/STYRELSE ancestor is found
    /// within the maximum traversal depth.</exception>
    public async Task<string> ResolveTreeRootOrgIdAsync(
        string primaryOrgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // Recursive CTE: walk from the given org upward through parent_org_id until
        // we find a MINISTRY or STYRELSE. Max depth ~5 in Danish state sector hierarchy.
        await using var cmd = new NpgsqlCommand(
            """
            WITH RECURSIVE ancestors AS (
                SELECT org_id, org_type, parent_org_id, 1 AS depth
                FROM organizations
                WHERE org_id = @primaryOrgId AND is_active = TRUE
                UNION ALL
                SELECT o.org_id, o.org_type, o.parent_org_id, a.depth + 1
                FROM organizations o
                INNER JOIN ancestors a ON o.org_id = a.parent_org_id
                WHERE o.is_active = TRUE AND a.depth < 10
            )
            SELECT org_id FROM ancestors
            WHERE org_type IN ('MINISTRY', 'STYRELSE')
            ORDER BY depth ASC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("primaryOrgId", primaryOrgId);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException(
                $"No MINISTRY or STYRELSE ancestor found for org_id='{primaryOrgId}'. " +
                "Cannot resolve reporting tree root.");
        }
        return (string)result;
    }

    /// <summary>
    /// Validates that the employee and manager belong to the same reporting tree by
    /// resolving both users' <c>primary_org_id</c> to their respective tree roots.
    /// </summary>
    /// <returns>The common <c>tree_root_org_id</c>.</returns>
    /// <exception cref="CrossTreeAssignmentException">If the employee and manager belong
    /// to different reporting trees.</exception>
    /// <exception cref="InvalidOperationException">If either user is not found.</exception>
    public async Task<string> ValidateSameTreeAsync(
        string employeeId, string managerId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // Fetch primary_org_id for both users in a single query.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT user_id, primary_org_id FROM users
            WHERE user_id IN (@employeeId, @managerId) AND is_active = TRUE
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("managerId", managerId);

        string? employeeOrgId = null;
        string? managerOrgId = null;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var userId = reader.GetString(reader.GetOrdinal("user_id"));
            var orgId = reader.GetString(reader.GetOrdinal("primary_org_id"));
            if (userId == employeeId) employeeOrgId = orgId;
            if (userId == managerId) managerOrgId = orgId;
        }

        if (employeeOrgId is null)
            throw new InvalidOperationException($"Employee user_id='{employeeId}' not found or inactive.");
        if (managerOrgId is null)
            throw new InvalidOperationException($"Manager user_id='{managerId}' not found or inactive.");

        var employeeTreeRoot = await ResolveTreeRootOrgIdAsync(employeeOrgId, ct);
        var managerTreeRoot = await ResolveTreeRootOrgIdAsync(managerOrgId, ct);

        if (employeeTreeRoot != managerTreeRoot)
        {
            throw new CrossTreeAssignmentException(
                $"Manager '{managerId}' (tree root '{managerTreeRoot}') and employee '{employeeId}' " +
                $"(tree root '{employeeTreeRoot}') belong to different reporting trees.");
        }

        return employeeTreeRoot;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Approval delegation — designated approver resolution (ADR-027 D5)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the designated approver for an employee per ADR-027 D5:
    /// ACTING → PRIMARY → recurse up inactive chain → NULL.
    /// Returns (managerId, approvalMethod, depth) where approvalMethod is one of
    /// "ACTING_MANAGER", "DESIGNATED_MANAGER", or null (caller uses ORG_SCOPE_FALLBACK).
    /// Depth indicates how many levels the traversal walked up through inactive managers;
    /// the caller can use depth > 3 to emit a <see cref="StatsTid.SharedKernel.Events.FallbackTraversalWarning"/>.
    /// </summary>
    public async Task<(string? ManagerId, string? ApprovalMethod, int Depth)> ResolveDesignatedApproverAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var currentEmployeeId = employeeId;
        var depth = 0;

        while (depth < 10)
        {
            // 1. Check ACTING line — takes precedence over PRIMARY.
            var actingLine = await GetActiveLineAsync(conn, currentEmployeeId, "ACTING", ct);
            if (actingLine is not null)
            {
                var isActive = await IsUserActiveAsync(conn, actingLine.ManagerId, ct);
                if (isActive)
                    return (actingLine.ManagerId, "ACTING_MANAGER", depth);
            }

            // 2. Check PRIMARY line.
            var primaryLine = await GetActiveLineAsync(conn, currentEmployeeId, "PRIMARY", ct);
            if (primaryLine is null)
                return (null, null, depth); // No reporting line — org-scope fallback.

            var primaryManagerActive = await IsUserActiveAsync(conn, primaryLine.ManagerId, ct);
            if (primaryManagerActive)
                return (primaryLine.ManagerId, "DESIGNATED_MANAGER", depth);

            // 3. Manager is inactive — walk up the chain.
            currentEmployeeId = primaryLine.ManagerId;
            depth++;
        }

        return (null, null, depth); // Depth exceeded — org-scope fallback.
    }

    private static async Task<ReportingLine?> GetActiveLineAsync(
        NpgsqlConnection conn, string employeeId, string relationship, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE employee_id = @employeeId
              AND relationship = @relationship
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("relationship", relationship);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    private static async Task<bool> IsUserActiveAsync(
        NpgsqlConnection conn, string userId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT is_active FROM users WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is true;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Lock + precondition (internal, following LocalAgreementProfileRepository)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires a row-level lock on the currently-active reporting line (if any) for the
    /// (employee_id, relationship) pair via <c>SELECT ... FOR UPDATE</c>. Returns the locked
    /// row as a full <see cref="ReportingLine"/>, or <c>null</c> if no line is currently active.
    /// Concurrent writers attempting the same lock serialize on this query.
    /// </summary>
    internal static async Task<ReportingLine?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string relationship,
        CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE employee_id = @employeeId
              AND relationship = @relationship
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("employeeId", employeeId);
        lockCmd.Parameters.AddWithValue("relationship", relationship);
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// Applies the optimistic-concurrency precondition (HTTP If-Match / If-None-Match)
    /// against the version of the row actually present at lock time. Throws
    /// <see cref="OptimisticConcurrencyException"/> on mismatch.
    /// </summary>
    internal static void ValidatePrecondition(ReportingLine? current, long? expectedVersion)
    {
        if (expectedVersion is null && current is not null)
        {
            throw new OptimisticConcurrencyException(
                $"Cannot create: an active reporting line already exists at version {current.Version}; " +
                $"use If-Match: \"{current.Version}\" for supersession.",
                expectedVersion: null,
                actualVersion: current.Version);
        }
        if (expectedVersion is not null && current is null)
        {
            throw new OptimisticConcurrencyException(
                $"No active reporting line exists, but caller sent If-Match: \"{expectedVersion.Value}\"; " +
                "the line may have been removed.",
                expectedVersion: expectedVersion,
                actualVersion: null);
        }
        if (expectedVersion is not null && current is not null && current.Version != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Current reporting line version is {current.Version}, " +
                $"but caller sent If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: current.Version);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Private SQL helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Closes the given reporting line by setting <c>effective_to</c> to
    /// <paramref name="effectiveTo"/> and bumping <c>version</c>. Used by
    /// <see cref="AssignAsync"/> for supersession.
    /// </summary>
    private static async Task CloseLineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid reportingLineId, DateOnly effectiveTo, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            """
            UPDATE reporting_lines
            SET effective_to = @effectiveTo, version = version + 1
            WHERE reporting_line_id = @reportingLineId
            """, conn, tx);
        closeCmd.Parameters.AddWithValue("effectiveTo", effectiveTo);
        closeCmd.Parameters.AddWithValue("reportingLineId", reportingLineId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Closes the given reporting line at CURRENT_DATE with version bump,
    /// returning the full closed row. Used by <see cref="RemoveAsync"/>.
    /// </summary>
    private static async Task<ReportingLine> CloseAndReturnLineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid reportingLineId, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            """
            UPDATE reporting_lines
            SET effective_to = CURRENT_DATE, version = version + 1
            WHERE reporting_line_id = @reportingLineId
            RETURNING *
            """, conn, tx);
        closeCmd.Parameters.AddWithValue("reportingLineId", reportingLineId);
        await using var reader = await closeCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException(
                $"CloseAndReturnLineAsync produced no row for reporting_line_id={reportingLineId}; " +
                "FOR UPDATE invariant violated.");
        }
        return MapReader(reader);
    }

    /// <summary>
    /// Inserts a new reporting line row at the supplied version.
    /// </summary>
    private static async Task InsertLineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        ReportingLine newLine, Guid newId, long version, DateTime createdAt,
        CancellationToken ct)
    {
        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO reporting_lines (
                reporting_line_id, employee_id, manager_id, tree_root_org_id,
                relationship, effective_from, effective_to,
                source, version, scheduled_expiry, created_by, created_at)
            VALUES (
                @reportingLineId, @employeeId, @managerId, @treeRootOrgId,
                @relationship, @effectiveFrom, NULL,
                @source, @version, @scheduledExpiry, @createdBy, @createdAt)
            """, conn, tx);
        insertCmd.Parameters.AddWithValue("reportingLineId", newId);
        insertCmd.Parameters.AddWithValue("employeeId", newLine.EmployeeId);
        insertCmd.Parameters.AddWithValue("managerId", newLine.ManagerId);
        insertCmd.Parameters.AddWithValue("treeRootOrgId", newLine.TreeRootOrgId);
        insertCmd.Parameters.AddWithValue("relationship", newLine.Relationship);
        insertCmd.Parameters.AddWithValue("effectiveFrom", newLine.EffectiveFrom);
        insertCmd.Parameters.AddWithValue("source", newLine.Source);
        insertCmd.Parameters.AddWithValue("version", version);
        insertCmd.Parameters.AddWithValue("scheduledExpiry", (object?)newLine.ScheduledExpiry?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("createdBy", newLine.CreatedBy);
        insertCmd.Parameters.AddWithValue("createdAt", createdAt);
        await insertCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reads multiple reporting lines from a command.
    /// </summary>
    private static async Task<IReadOnlyList<ReportingLine>> ReadLinesAsync(
        NpgsqlCommand cmd, CancellationToken ct)
    {
        var lines = new List<ReportingLine>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lines.Add(MapReader(reader));
        return lines;
    }

    private static ReportingLine MapReader(NpgsqlDataReader reader) => new()
    {
        ReportingLineId = reader.GetGuid(reader.GetOrdinal("reporting_line_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        ManagerId = reader.GetString(reader.GetOrdinal("manager_id")),
        TreeRootOrgId = reader.GetString(reader.GetOrdinal("tree_root_org_id")),
        Relationship = reader.GetString(reader.GetOrdinal("relationship")),
        EffectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_from"))),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
            ? null
            : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_to"))),
        Source = reader.GetString(reader.GetOrdinal("source")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        ScheduledExpiry = reader.IsDBNull(reader.GetOrdinal("scheduled_expiry"))
            ? null
            : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("scheduled_expiry"))),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
    };
}

/// <summary>
/// Thrown by <see cref="ReportingLineRepository.ValidateSameTreeAsync"/> when the manager
/// and employee belong to different reporting trees (different MINISTRY/STYRELSE roots).
/// The endpoint maps this to <c>422 Unprocessable Entity</c>.
/// </summary>
public sealed class CrossTreeAssignmentException : Exception
{
    public CrossTreeAssignmentException(string message)
        : base(message) { }

    public CrossTreeAssignmentException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thrown when an operation would create a second root in a reporting tree, violating
/// the single-root invariant.
/// </summary>
public sealed class RootInvariantViolationException : Exception
{
    public string TreeRootOrgId { get; }

    public RootInvariantViolationException(string treeRootOrgId)
        : base($"Operation would create a second root in tree '{treeRootOrgId}'.")
    {
        TreeRootOrgId = treeRootOrgId;
    }

    public RootInvariantViolationException(string treeRootOrgId, string message)
        : base(message)
    {
        TreeRootOrgId = treeRootOrgId;
    }

    public RootInvariantViolationException(string treeRootOrgId, string message, Exception innerException)
        : base(message, innerException)
    {
        TreeRootOrgId = treeRootOrgId;
    }
}
