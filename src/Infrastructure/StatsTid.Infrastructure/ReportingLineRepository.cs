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
    private readonly ManagerVikarRepository _vikarRepo;

    /// <summary>
    /// Primary constructor (DI). The <paramref name="vikarRepo"/> is consumed by
    /// <see cref="ResolveDesignatedApproverAsync"/> for the S74 vikar-consult (ADR-027 D5);
    /// it is OPTIONAL so existing tests that construct the repository with the factory
    /// alone keep compiling — when omitted, a vikar repo is derived from the same factory.
    /// </summary>
    public ReportingLineRepository(DbConnectionFactory connectionFactory, ManagerVikarRepository? vikarRepo = null)
    {
        _connectionFactory = connectionFactory;
        _vikarRepo = vikarRepo ?? new ManagerVikarRepository(connectionFactory);
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
    /// In-transaction sibling overload: returns BOTH active PRIMARY and ACTING lines where the
    /// employee is the SUBJECT (employee_id), reusing the caller-supplied
    /// <paramref name="conn"/> + <paramref name="tx"/> so the read participates in the same
    /// atomic transaction. Used by the S74 R10 delete-with-reassignment to find + close the
    /// removed person's OWN outgoing edges within the closure tx (and to see any same-tx
    /// changes already applied).
    /// </summary>
    public async Task<IReadOnlyList<ReportingLine>> GetActiveByEmployeeInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM reporting_lines
            WHERE employee_id = @employeeId
              AND effective_to IS NULL
            ORDER BY relationship
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
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
        // S74-7403 B1: ReadCommitted (not RepeatableRead). A RepeatableRead snapshot is pinned at
        // the FIRST statement of the tx — which, on the lock-serialized assign paths, is the
        // SELECT pg_advisory_xact_lock(...) that runs BEFORE the lock is granted. After a competing
        // tx commits a new edge and releases, this tx would still read the PRE-commit snapshot and
        // miss the edge. ReadCommitted gives each post-lock statement a fresh snapshot that sees the
        // just-committed state, which is correct for a lock-serialized critical section (the
        // FOR UPDATE in AcquireLockAsync + the optimistic version checks still hold).
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
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
        // S74-7403 B1: ReadCommitted (not RepeatableRead) — same rationale as AssignAsync; the
        // lock-serialized critical section needs each post-lock statement to see committed state.
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
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
    //  Helper / Organisation-membership methods
    // ──────────────────────────────────────────────────────────────────────
    //
    // S95 / ADR-035 slice 4 — the tree-WALK machinery is RETIRED. Post-S92 every
    // user's reporting "tree root" is provably their own primary_org_id: the former
    // ResolveTreeRootOrgIdAsync walked up parent_org_id to the first org_type IN
    // ('MAO','ORGANISATION'), and since BOTH permitted types are terminal the walk
    // ALWAYS returned the input org at depth 1. So tree_root == primary_org for every
    // user, ALWAYS. We therefore read primary_org_id DIRECTLY (no CTE) — byte-identical
    // to the old walk result — and the lock domain (the advisory keyed on primary_org)
    // is unchanged. ValidateSameTreeAsync → ValidateSameOrganisationAsync below.

    /// <summary>
    /// Validates that the employee and manager belong to the same Organisation by
    /// reading both users' <c>primary_org_id</c> directly and comparing them for equality.
    /// (S95 / ADR-035 slice 4: replaces the retired tree-WALK — post-S92 the "tree root"
    /// of a user IS their <c>primary_org_id</c>, so a direct equality of the two homes is
    /// byte-identical to the old <c>ValidateSameTreeAsync</c> result.)
    /// </summary>
    /// <returns>The common <c>primary_org_id</c> (the Organisation), stored in
    /// <c>reporting_lines.tree_root_org_id</c> — the same value the walk produced.</returns>
    /// <exception cref="CrossOrganisationAssignmentException">If the employee and manager belong
    /// to different Organisations.</exception>
    /// <exception cref="InvalidOperationException">If either user is not found.</exception>
    public async Task<string> ValidateSameOrganisationAsync(
        string employeeId, string managerId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ValidateSameOrganisationAsync(conn, tx: null, employeeId, managerId, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="ValidateSameOrganisationAsync(string, string, CancellationToken)"/>. Reuses the
    /// caller-supplied <paramref name="conn"/> (+ optional <paramref name="tx"/>) so the
    /// reads see the same transaction's uncommitted state — REQUIRED by S74 R9's atomic
    /// create+assign, where the new user row is inserted earlier in the SAME tx and a
    /// fresh-connection read would not yet see it. With <paramref name="tx"/> = null this is a
    /// plain connection-reusing read (the self-contained overload above delegates to it).
    ///
    /// <para>
    /// <b>S95 / ADR-035 slice 4 — the tree-WALK is gone; this compares <c>primary_org_id</c>
    /// directly.</b> Post-S92 a user's reporting "tree root" IS their <c>primary_org_id</c>
    /// (the former walk always returned the input org at depth 1), so same-Organisation is a
    /// direct equality of the two homes — byte-identical to the old same-tree result and the
    /// same Organisation value stored in <c>reporting_lines.tree_root_org_id</c>.
    /// </para>
    ///
    /// <para>
    /// <b>S74-7403 B1 — BOTH user rows are pinned <c>FOR UPDATE</c> (cross-Organisation-edge race).</b>
    /// The prior pass pinned only the EMPLOYEE row, leaving the MANAGER row unpinned: a concurrent
    /// cross-Organisation transfer (a PUT moving the manager's <c>primary_org_id</c>) could move the
    /// manager AFTER this check but BEFORE the edge insert, producing a cross-Organisation edge
    /// (ADR-027 D2 violation). This method KEEPS locking BOTH the employee and the manager
    /// <c>users</c> rows <c>FOR UPDATE</c> in one statement, ORDERED BY <c>user_id</c> — so both
    /// homes are pinned for the whole tx (neither party can be transferred mid-assign) AND any two
    /// concurrent assigns over an overlapping pair acquire the row locks in the SAME id-sorted
    /// order, so they cannot deadlock against each other.
    /// </para>
    ///
    /// <para>
    /// <b>Total lock order (consistent on EVERY path — see <c>ReportingLineEndpoints</c>; matches
    /// ADR-027:149 advisory→rows):</b> (1) the per-Organisation advisory lock — taken by the caller
    /// BEFORE this method, via <see cref="AcquireTreeLockForEmployeeAsync"/> (S78 R9, the
    /// drift-guarded acquire) on the employee's current Organisation; (2) the two user rows HERE,
    /// <c>FOR UPDATE</c> in <c>user_id</c> order (so any two concurrent assigns over an overlapping
    /// pair lock the shared rows in the same order — deadlock-safe); (3)
    /// <see cref="GuardNoCycleAsync"/>; (4) <see cref="AcquireLockAsync"/>'s slot <c>FOR UPDATE</c>
    /// on a <c>reporting_lines</c> row (a different table, locked last). The advisory is ALWAYS taken
    /// before any user row, so a transaction parked on the advisory holds NO user row and cannot
    /// deadlock the advisory holder; the id-ordered two-row pin closes the cross-Organisation-edge
    /// race against a concurrent transfer.
    /// </para>
    /// </summary>
    public async Task<string> ValidateSameOrganisationAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, string managerId, CancellationToken ct = default)
    {
        // S74-7403 B1: pin BOTH the employee AND the manager rows FOR UPDATE, ORDERED BY user_id.
        // The id-sorted lock guarantees deadlock-safety (two concurrent assigns over an overlapping
        // pair lock the shared rows in the same order); the FOR UPDATE pins both homes so neither
        // party can be transferred between this same-Organisation validation and the edge insert (the
        // cross-Organisation-edge race, ADR-027 D2). When employee == manager (the self-assign
        // degenerate) ANY(@ids) collapses to a single row — harmless; the cycle guard rejects the
        // self-edge.
        var ids = new[] { employeeId, managerId };
        await using var cmd = new NpgsqlCommand(
            """
            SELECT user_id, primary_org_id FROM users
            WHERE user_id = ANY(@ids) AND is_active = TRUE
            ORDER BY user_id
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("ids", ids);

        string? employeeOrgId = null;
        string? managerOrgId = null;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var userId = reader.GetString(reader.GetOrdinal("user_id"));
                var orgId = reader.GetString(reader.GetOrdinal("primary_org_id"));
                if (userId == employeeId) employeeOrgId = orgId;
                if (userId == managerId) managerOrgId = orgId;
            }
        }

        if (employeeOrgId is null)
            throw new InvalidOperationException($"Employee user_id='{employeeId}' not found or inactive.");
        if (managerOrgId is null)
            throw new InvalidOperationException($"Manager user_id='{managerId}' not found or inactive.");

        // S95: the user's Organisation IS their primary_org_id (no walk). Same-Organisation is a
        // direct equality — byte-identical to the retired same-tree resolution.
        if (!string.Equals(employeeOrgId, managerOrgId, StringComparison.Ordinal))
        {
            throw new CrossOrganisationAssignmentException(
                $"Manager '{managerId}' (Organisation '{managerOrgId}') and employee '{employeeId}' " +
                $"(Organisation '{employeeOrgId}') belong to different Organisations.");
        }

        return employeeOrgId;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Cycle guard (S74 R8) — tree-wide advisory lock + bounded descendant walk
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// S74-7403 B3: an effectively-unbounded safety ceiling on the descendant cycle-walk — NOT a
    /// real depth limit. The previous value (10) was a FALSE-NEGATIVE source: a manager at
    /// descendant depth 11+ was never reached, so a real deep cycle was ADMITTED. The walk's true
    /// termination guarantee is the path-array visited-set guard
    /// (<c>NOT (rl.employee_id = ANY(d.path))</c>), which terminates even on a pre-existing loop;
    /// this ceiling is a belt-and-suspenders backstop set far above any conceivable real tree depth
    /// (Danish state-sector trees are ≤ ~5 deep). The walk must traverse the FULL descendant set to
    /// catch deep cycles, so this is raised to 10_000 rather than bounding the real walk.
    /// </summary>
    private const int CycleWalkMaxDepth = 10_000;

    /// <summary>
    /// S74 R8 — takes a STABLE, tree-wide advisory lock keyed on the reporting
    /// <paramref name="treeRootOrgId"/>, so EVERY assign within one tree serializes through the
    /// cycle check. xact-scoped (auto-released at COMMIT/ROLLBACK; no manual unlock). Mirrors the
    /// ADR-032 D4 employee advisory-lock idiom (<c>pg_advisory_xact_lock(hashtext(...))</c>) but
    /// keyed on the tree root instead of an employee.
    ///
    /// <para>
    /// <b>Why the slot <c>FOR UPDATE</c> alone is not enough (the phantom gap).</b>
    /// <see cref="AcquireLockAsync"/> locks only the target (employee, relationship) SLOT.
    /// Two concurrent FIRST assignments — say A→B and B→A — find NO existing row to lock, so
    /// neither blocks the other, and they can each commit half of a 2-cycle. A descendant
    /// <c>SELECT … FOR UPDATE</c> has the same gap (the rows it would lock don't exist yet).
    /// Serializing all of a tree's assigns through this one advisory key closes the gap:
    /// the cycle-walk of the second assign sees the first's committed edge (or blocks until it
    /// commits/rolls back). Taken FIRST, before the descendant walk, on EVERY assign path.
    /// </para>
    /// </summary>
    public static async Task AcquireTreeLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string treeRootOrgId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('reporting-tree-' || @treeRootOrgId))", conn, tx);
        cmd.Parameters.AddWithValue("treeRootOrgId", treeRootOrgId);
        await cmd.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// S78 R9 — derives the CURRENT advisory key (the Organisation) for <paramref name="employeeId"/>
    /// from the live (un-pinned) <c>users.primary_org_id</c>, reusing the caller's in-flight
    /// <c>conn</c>+<c>tx</c> so it reads the same transaction's committed/uncommitted state. NO
    /// <c>FOR UPDATE</c> — the advisory is the first lock; user rows are pinned only later (in
    /// <see cref="ValidateSameOrganisationAsync"/>).
    ///
    /// <para>
    /// S95 / ADR-035 slice 4 — the tree-WALK is RETIRED: post-S92 a user's reporting "tree root" IS
    /// their <c>primary_org_id</c> (the former walk always returned the input org at depth 1), so this
    /// reads <c>primary_org_id</c> DIRECTLY — byte-identical to the old <c>ResolveTreeRootOrgIdAsync</c>
    /// result. The method name and the <c>reporting-tree-</c> lock prefix are KEPT (internal; renaming
    /// them risks a divergent advisory — a cosmetic follow-up).
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">If the employee is missing/inactive — the caller
    /// surfaces a clean 400/404.</exception>
    private static async Task<string> DeriveEmployeeTreeRootInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        string? primaryOrgId;
        await using (var cmd = new NpgsqlCommand(
            "SELECT primary_org_id FROM users WHERE user_id = @userId AND is_active = TRUE", conn, tx))
        {
            cmd.Parameters.AddWithValue("userId", employeeId);
            primaryOrgId = (string?)await cmd.ExecuteScalarAsync(ct);
        }
        if (primaryOrgId is null)
            throw new InvalidOperationException($"Employee user_id='{employeeId}' not found or inactive.");

        // S95: the user's Organisation (the advisory key) IS their primary_org_id — no walk.
        return primaryOrgId;
    }

    /// <summary>
    /// S78 R9 — THE SHARED DRIFT-GUARDED ACQUIRE (the load-bearing concurrency primitive). Takes the
    /// <c>reporting-tree</c> advisory for <paramref name="employeeId"/>'s CURRENT tree root and PROVES the
    /// key is not stale, closing the S74-7403 cross-styrelse-transfer drift on every employee-current-root
    /// mutator (reporting-line assign / remove / acting, and the transfer's old+new roots).
    ///
    /// <para>
    /// The advisory key derives from the mutable <c>users.primary_org_id</c> via an UNLOCKED read, so a
    /// concurrent transfer (<c>PUT /api/admin/users/{userId}</c> changing <c>primary_org_id</c>) that
    /// commits between the derive and the <c>pg_advisory_xact_lock</c> would leave this mutator holding a
    /// STALE key — two paths on different keys, no mutual exclusion. This method:
    /// </para>
    /// <list type="number">
    /// <item><description>derives the current tree root (UNLOCKED);</description></item>
    /// <item><description>acquires the <c>reporting-tree-{root}</c> xact advisory on it;</description></item>
    /// <item><description>RE-DERIVES the root UNDER the held advisory. If it DRIFTED (a transfer committed
    /// in between), it throws <see cref="TreeRootDriftException"/> — it does NOT release+re-acquire in-tx
    /// (impossible with <c>pg_advisory_xact_lock</c>, which releases only at tx end) and does NOT retain
    /// the old key while taking the new (lock-accumulation / A↔B deadlock). The caller ROLLS BACK and
    /// RETRIES the whole request on a fresh tx.</description></item>
    /// </list>
    ///
    /// <para>
    /// Returns the verified (non-stale) tree root. The drift check runs BEFORE any user-row
    /// <c>FOR UPDATE</c> or mutation, so a rolled-back attempt has NO side effects. NOTE: this re-derive
    /// is correct because the advisory serializes the TREE, but a transfer changes a USER's org — so the
    /// re-derive under the lock still observes a transfer that committed during the acquire wait (it reads
    /// committed state, ReadCommitted). The lock makes the post-acquire root STABLE for the holder's
    /// duration: once we hold the key for the (re-derived) root, no in-tree mutator can proceed on it, and
    /// any transfer of THIS employee would itself have to re-derive + drift-guard, so it cannot silently
    /// move the employee out from under a held key without the next mutator detecting drift.
    /// </para>
    /// </summary>
    /// <exception cref="TreeRootDriftException">If the root drifted under the advisory (caller retries).</exception>
    /// <exception cref="InvalidOperationException">If the employee is missing/inactive or has no
    /// MAO/ORGANISATION ancestor.</exception>
    public async Task<string> AcquireTreeLockForEmployeeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct = default)
    {
        // (1) derive the current tree root (UNLOCKED).
        var preAcquireRoot = await DeriveEmployeeTreeRootInTxAsync(conn, tx, employeeId, ct);

        // (2) acquire the reporting-tree advisory on it.
        await AcquireTreeLockAsync(conn, tx, preAcquireRoot, ct);

        // (3) RE-DERIVE under the held advisory. A transfer that committed between (1) and (2) is now
        //     visible (ReadCommitted); if the root moved, the key we hold is stale → signal a retry.
        var postAcquireRoot = await DeriveEmployeeTreeRootInTxAsync(conn, tx, employeeId, ct);
        if (!string.Equals(preAcquireRoot, postAcquireRoot, StringComparison.Ordinal))
            throw new TreeRootDriftException(employeeId, preAcquireRoot, postAcquireRoot);

        return postAcquireRoot;
    }

    /// <summary>
    /// S78 R9 (R3) — the TRANSFER variant of the drift-guarded acquire. A cross-styrelse transfer
    /// (<c>PUT /api/admin/users/{userId}</c> moving <c>primary_org_id</c> from one styrelse tree to
    /// another) must hold the <c>reporting-tree</c> advisory for BOTH the OLD (current) and the NEW
    /// (target) tree roots BEFORE it pins the users row + UPDATEs the org — so that an assign/remove in
    /// EITHER tree blocks against the move (the employee is leaving the OLD tree and entering the NEW one).
    ///
    /// <para>
    /// Derives the OLD root from <paramref name="employeeId"/>'s LIVE org and the NEW root from
    /// <paramref name="newPrimaryOrgId"/>, then acquires the two advisories in DETERMINISTIC id-sorted
    /// order (so two concurrent transfers over the same tree pair acquire in the same order — deadlock-
    /// safe), then RE-DERIVES the OLD root under the held locks. If the OLD root DRIFTED (a different
    /// transfer of this same employee committed between the derive and the acquire), it throws
    /// <see cref="TreeRootDriftException"/> and the caller rolls back + retries on a fresh tx. The NEW root
    /// derives from the request-fixed target org (not a mutable user row), so it cannot drift within the
    /// request. Both locks are taken BEFORE any user-row <c>FOR UPDATE</c> (advisory → rows order).
    /// </para>
    ///
    /// <para>
    /// When the OLD and NEW roots are the SAME (an intra-tree org move) only one advisory is taken (the
    /// id-sort dedupes) — still correct: a single tree is fully serialized.
    /// </para>
    /// </summary>
    /// <returns>The verified (OLD, NEW) tree roots.</returns>
    /// <exception cref="TreeRootDriftException">If the OLD root drifted under the advisory (caller retries).</exception>
    /// <exception cref="InvalidOperationException">If the employee/org cannot be resolved to a tree root.</exception>
    public async Task<(string OldRoot, string NewRoot)> AcquireTreeLocksForTransferAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, string newPrimaryOrgId,
        CancellationToken ct = default)
    {
        // (1) derive BOTH roots UNLOCKED: OLD from the employee's live org, NEW from the target org.
        //     S95 — the user's Organisation (the advisory key) IS their primary_org_id; the NEW key is
        //     the request-fixed target org directly (no walk), so it cannot drift within the request.
        var preOldRoot = await DeriveEmployeeTreeRootInTxAsync(conn, tx, employeeId, ct);
        var newRoot = newPrimaryOrgId;

        // (2) acquire the two advisories in id-sorted order (deadlock-safe across concurrent transfers).
        //     Distinct + sorted: when OLD == NEW only one key is taken.
        foreach (var root in new[] { preOldRoot, newRoot }.Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal))
            await AcquireTreeLockAsync(conn, tx, root, ct);

        // (3) RE-DERIVE the OLD root under the held locks. A concurrent transfer of THIS employee that
        //     committed between (1) and (2) is now visible → the OLD key we hold is stale → retry signal.
        var postOldRoot = await DeriveEmployeeTreeRootInTxAsync(conn, tx, employeeId, ct);
        if (!string.Equals(preOldRoot, postOldRoot, StringComparison.Ordinal))
            throw new TreeRootDriftException(employeeId, preOldRoot, postOldRoot);

        return (postOldRoot, newRoot);
    }

    /// <summary>
    /// S83 / ADR-027 D18→D19 — THE REVOKE-SAFE drift-guarded acquire (the third member of the
    /// drift-guarded acquire family, alongside <see cref="AcquireTreeLockForEmployeeAsync"/> and
    /// <see cref="AcquireTreeLocksForTransferAsync"/>). It closes the two reporting-edge revoke
    /// serialization gaps left by S78 D18: the self-<c>/delegate</c> DELETE took NO advisory at all,
    /// and the admin-vikar DELETE took ONLY the PERSISTED-root advisory — so a concurrent
    /// cross-styrelse transfer of the revoke SUBJECT, or a key-sharing mutator on the subject's
    /// CURRENT tree, could proceed on a DIFFERENT key with no mutual exclusion against the revoke.
    ///
    /// <para>
    /// The revoke must remain SAFE even when the subject is inactive/transferred — so the PERSISTED
    /// <c>manager_vikar.tree_root_org_id</c> (<paramref name="persistedRoot"/>) is the immutable
    /// revoke-authority anchor and is ALWAYS locked (it is a fixed column on the row and cannot drift).
    /// On top of that this method ALSO locks the subject's CURRENT tree root when one can be derived,
    /// so the revoke serializes against in-tree mutators / a transfer on the live tree too. Crucially
    /// the current-root derivation is DEFENSIVE: <see cref="DeriveEmployeeTreeRootInTxAsync"/> throws
    /// <see cref="InvalidOperationException"/> when the subject is missing/inactive (its
    /// <c>WHERE … AND is_active = TRUE</c> filter), and we SWALLOW that throw — "no current root" —
    /// rather than pre-gating on a separate <c>SELECT is_active</c>. Catching the derive's throw is
    /// what closes the active→inactive race: a deactivation that commits mid-request must never 500 a
    /// revoke-safe path; it simply collapses to the persisted-only lock set.
    /// </para>
    ///
    /// <list type="number">
    /// <item><description>Always lock <paramref name="persistedRoot"/> (the immutable anchor).</description></item>
    /// <item><description>DEFENSIVE-DERIVE the subject's current root (swallow the missing/inactive throw).</description></item>
    /// <item><description>Acquire the union <c>{persistedRoot} ∪ {currentRoot?}</c> in the SAME distinct +
    /// id-sorted order as <see cref="AcquireTreeLocksForTransferAsync"/> — uniform lock ordering, so a
    /// revoke and a transfer over the same tree pair are deadlock-safe against each other. All advisories
    /// are taken BEFORE any caller row <c>FOR UPDATE</c> (advisory → rows order).</description></item>
    /// <item><description>DRIFT-GUARD THE CURRENT ROOT ONLY (the persisted root cannot drift): if a current
    /// root was derived, RE-DERIVE it under the held locks — again defensively (a subject that deactivated
    /// UNDER the lock now throws → treat as "no current root", fall back to persisted-only, NO error). If the
    /// re-derived current root DIFFERS from the first, throw <see cref="TreeRootDriftException"/> so the
    /// caller's <c>TreeRootDriftRetry.RunAsync</c> rolls back + retries on a fresh tx (a concurrent transfer
    /// committed between the derive and the acquire → the current key we hold is stale).</description></item>
    /// </list>
    ///
    /// <para>
    /// Returns the set of locked roots (the callers do not need it — they only need the locks held +
    /// drift signalled — but it is convenient for assertions / diagnostics).
    /// </para>
    /// </summary>
    /// <param name="persistedRoot">The immutable revoke-authority anchor (the persisted
    /// <c>manager_vikar.tree_root_org_id</c>); always locked.</param>
    /// <param name="subjectId">The revoke subject (the absent approver / manager) whose CURRENT tree
    /// root is additionally locked when derivable.</param>
    /// <exception cref="TreeRootDriftException">If the subject's CURRENT root drifted under the advisory
    /// (caller retries on a fresh tx).</exception>
    public async Task<IReadOnlyCollection<string>> AcquireRevokeTreeLocksAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string persistedRoot, string subjectId,
        CancellationToken ct = default)
    {
        // (1) DEFENSIVE-DERIVE the subject's CURRENT root (UNLOCKED). Swallow the missing/inactive
        //     throw — that is the active→inactive-race close: no current root, not a 500.
        string? preAcquireCurrentRoot = null;
        try
        {
            preAcquireCurrentRoot = await DeriveEmployeeTreeRootInTxAsync(conn, tx, subjectId, ct);
        }
        catch (InvalidOperationException)
        {
            // Subject missing/inactive (or no MAO/ORGANISATION ancestor) → no derivable current root.
            // The persisted root alone keeps the revoke safe.
        }

        // (2) Acquire the union {persistedRoot} ∪ {currentRoot?} in distinct + id-sorted order (the
        //     EXACT idiom AcquireTreeLocksForTransferAsync uses — uniform ordering, deadlock-safe vs
        //     the transfer path). When the current root equals the persisted root only one key is taken.
        var roots = preAcquireCurrentRoot is null
            ? new[] { persistedRoot }
            : new[] { persistedRoot, preAcquireCurrentRoot };
        var lockedRoots = roots.Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToArray();
        foreach (var root in lockedRoots)
            await AcquireTreeLockAsync(conn, tx, root, ct);

        // (3) DRIFT-GUARD the CURRENT root only (the persisted root is a fixed column — it cannot drift).
        //     Re-derive under the held locks, again DEFENSIVELY: if the subject deactivated UNDER the
        //     lock the re-derive now throws → treat as "no current root", fall back to persisted-only,
        //     no error. If the re-derived current root differs from the first, the key we hold is stale.
        if (preAcquireCurrentRoot is not null)
        {
            string? postAcquireCurrentRoot = null;
            try
            {
                postAcquireCurrentRoot = await DeriveEmployeeTreeRootInTxAsync(conn, tx, subjectId, ct);
            }
            catch (InvalidOperationException)
            {
                // Subject deactivated under the held lock → no current root; persisted-only is still safe.
            }

            if (postAcquireCurrentRoot is not null
                && !string.Equals(preAcquireCurrentRoot, postAcquireCurrentRoot, StringComparison.Ordinal))
                throw new TreeRootDriftException(subjectId, preAcquireCurrentRoot, postAcquireCurrentRoot);
        }

        return lockedRoots;
    }

    /// <summary>
    /// S74 R8 — the cycle guard. REJECTS (via <see cref="ReportingCycleException"/>) an
    /// assignment whose chosen <paramref name="managerId"/> is the <paramref name="employeeId"/>
    /// itself OR any active-PRIMARY/ACTING descendant of the employee (which would create a
    /// reporting cycle). Run inside the caller's transaction AFTER
    /// <see cref="AcquireTreeLockAsync"/> + same-tree validation, on BOTH assign paths.
    ///
    /// <para>
    /// The self-case (manager == employee) is also blocked at the DB by the
    /// <c>CHECK (employee_id &lt;&gt; manager_id)</c>, but we reject it here too for a friendly
    /// 4xx instead of a raw 23514. The descendant walk follows <c>manager_id → employee_id</c>
    /// edges downward from the employee through active (<c>effective_to IS NULL</c>) lines. The walk
    /// traverses the FULL descendant set (S74-7403 B3 — no real depth cap); the path-array
    /// visited-set guard is the termination guarantee, with <see cref="CycleWalkMaxDepth"/> as an
    /// effectively-unbounded safety backstop only.
    /// </para>
    /// </summary>
    /// <exception cref="ReportingCycleException">If <paramref name="managerId"/> is the employee
    /// or one of the employee's descendants.</exception>
    public Task GuardNoCycleAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string managerId, CancellationToken ct = default)
        => GuardNoCycleAsync(conn, tx, employeeId, managerId, CycleWalkMaxDepth, ct);

    /// <summary>
    /// S74-7403 B3 — internal overload taking an explicit <paramref name="maxDepth"/> safety
    /// ceiling, so a test can run the descendant walk with the ceiling set ABOVE any conceivable
    /// loop length (e.g. <see cref="int.MaxValue"/>) to PROVE that the path-array visited-set guard
    /// — NOT the depth ceiling — is what terminates the walk on a pre-existing loop. Production
    /// callers use the parameterless overload, which passes <see cref="CycleWalkMaxDepth"/>.
    /// </summary>
    public async Task GuardNoCycleAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string managerId, int maxDepth, CancellationToken ct = default)
    {
        // Self-cycle: a person cannot be their own manager.
        if (string.Equals(employeeId, managerId, StringComparison.Ordinal))
            throw new ReportingCycleException(employeeId, managerId,
                $"Cannot assign '{managerId}' as the manager of '{employeeId}': a person cannot be their own manager.");

        // Descendant-cycle: if the chosen manager is somewhere BELOW the employee in the tree,
        // the new edge would close a loop. Walk downward (manager_id → employee_id) from the
        // employee and reject if we reach the manager. A single recursive CTE does the bounded
        // walk in one round-trip; the cycle-detection `path` array + depth bound guarantee
        // termination even if legacy data already contains a loop.
        await using var cmd = new NpgsqlCommand(
            """
            WITH RECURSIVE descendants AS (
                SELECT rl.employee_id, 1 AS depth, ARRAY[rl.manager_id, rl.employee_id] AS path
                FROM reporting_lines rl
                WHERE rl.manager_id = @employeeId
                  AND rl.effective_to IS NULL
                UNION ALL
                SELECT rl.employee_id, d.depth + 1, d.path || rl.employee_id
                FROM reporting_lines rl
                INNER JOIN descendants d ON rl.manager_id = d.employee_id
                WHERE rl.effective_to IS NULL
                  AND d.depth < @maxDepth
                  AND NOT (rl.employee_id = ANY(d.path))   -- guard against a pre-existing loop
            )
            SELECT 1 FROM descendants WHERE employee_id = @managerId LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("managerId", managerId);
        cmd.Parameters.AddWithValue("maxDepth", maxDepth);

        var hit = await cmd.ExecuteScalarAsync(ct);
        if (hit is not null)
            throw new ReportingCycleException(employeeId, managerId,
                $"Cannot assign '{managerId}' as the manager of '{employeeId}': '{managerId}' is a " +
                $"subordinate (descendant) of '{employeeId}', which would create a reporting cycle.");
    }

    /// <summary>
    /// S74-7404 R11b — returns the set of active-PRIMARY/ACTING <b>descendants</b> of
    /// <paramref name="employeeId"/> (everyone BELOW them in the reporting tree). Read-only sibling
    /// of <see cref="GuardNoCycleAsync"/>: it runs the SAME bounded downward
    /// <c>manager_id → employee_id</c> walk with the SAME path-array visited-set termination guard
    /// and the SAME <see cref="CycleWalkMaxDepth"/> safety ceiling — but instead of throwing on a
    /// match it RETURNS the descendant id set. The cycle guard could not be reused as-is (it
    /// short-circuits on the first hit and throws); this method materializes the whole subtree so
    /// the person-search picker can exclude self + descendants server-side (a person cannot pick a
    /// subordinate as their own approver — the cycle-prevention mirror for the picker, R11b). The
    /// returned set does NOT include <paramref name="employeeId"/> itself; the caller excludes self
    /// separately.
    /// </summary>
    public async Task<IReadOnlyCollection<string>> GetDescendantIdsAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // Identical descendant walk to GuardNoCycleAsync's CTE (downward manager_id → employee_id
        // over active lines; path-array guard terminates even on a pre-existing loop; the depth
        // bound is the effectively-unbounded safety backstop), but projecting the full id set.
        await using var cmd = new NpgsqlCommand(
            """
            WITH RECURSIVE descendants AS (
                SELECT rl.employee_id, 1 AS depth, ARRAY[rl.manager_id, rl.employee_id] AS path
                FROM reporting_lines rl
                WHERE rl.manager_id = @employeeId
                  AND rl.effective_to IS NULL
                UNION ALL
                SELECT rl.employee_id, d.depth + 1, d.path || rl.employee_id
                FROM reporting_lines rl
                INNER JOIN descendants d ON rl.manager_id = d.employee_id
                WHERE rl.effective_to IS NULL
                  AND d.depth < @maxDepth
                  AND NOT (rl.employee_id = ANY(d.path))   -- guard against a pre-existing loop
            )
            SELECT DISTINCT employee_id FROM descendants
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("maxDepth", CycleWalkMaxDepth);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetString(0));
        return ids;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  Approval delegation — designated approver resolution (ADR-027 D5)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the designated approver for an employee per ADR-027 D5, extended in S74
    /// (TASK-7401 R3) with the approver-owned <c>manager_vikar</c> consult. Single-winner
    /// precedence:
    /// <list type="number">
    /// <item><description>per-report admin-assigned ACTING (highest — the EXISTING check);</description></item>
    /// <item><description>the resolved PRIMARY manager M's active vikar V covering <paramref name="asOf"/>
    /// whose stand-in user is itself ACTIVE → return V as <c>ACTING_MANAGER</c>;</description></item>
    /// <item><description>M if active → <c>DESIGNATED_MANAGER</c>;</description></item>
    /// <item><description>inactive-manager escalation walk.</description></item>
    /// </list>
    /// Returns (managerId, approvalMethod, depth) where approvalMethod is one of
    /// "ACTING_MANAGER" (admin ACTING OR vikar — a vikar IS a stand-in/acting approver),
    /// "DESIGNATED_MANAGER", or null (caller uses ORG_SCOPE_FALLBACK).
    /// Depth indicates how many levels the traversal walked up through inactive managers;
    /// the caller can use depth > 3 to emit a <see cref="StatsTid.SharedKernel.Events.FallbackTraversalWarning"/>.
    ///
    /// <para>
    /// <paramref name="asOf"/> defaults to today (<c>null</c> ⇒ today) and sits AFTER
    /// <paramref name="ct"/> so the existing approve/reject callers
    /// (<c>ResolveDesignatedApproverAsync(employeeId, ct)</c>) compile UNCHANGED; 7402
    /// passes an explicit date via the named <c>asOf:</c> argument. The vikar covers
    /// <c>asOf</c> when <c>effective_to IS NULL AND until_date &gt;= asOf</c> (INCLUSIVE
    /// "til og med").
    /// </para>
    ///
    /// <para>
    /// Edge cases (R3, pinned): (a) if M is INACTIVE but holds an active vikar V whose
    /// stand-in user is ACTIVE, V WINS over escalation — and this fires in the SAME loop
    /// iteration where M is found inactive, BEFORE the walk advances; (b) if V's stand-in
    /// user is INACTIVE, the vikar is SKIPPED (it cannot grant usable authority) and
    /// resolution falls through to M-if-active else escalation.
    /// </para>
    /// </summary>
    public async Task<(string? ManagerId, string? ApprovalMethod, int Depth)> ResolveDesignatedApproverAsync(
        string employeeId, CancellationToken ct = default, DateOnly? asOf = null)
    {
        var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var currentEmployeeId = employeeId;
        var depth = 0;

        while (depth < 10)
        {
            // 1. Check ACTING line — admin-assigned ACTING takes precedence over everything.
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

            var primaryManagerId = primaryLine.ManagerId;

            // 2b. Vikar consult (S74 R3) — M's active approver-owned vikar V covering asOf,
            //     whose stand-in user is itself ACTIVE, beats BOTH M-if-active AND the
            //     inactive-M escalation walk. Keyed on THIS iteration's M (primaryManagerId)
            //     BEFORE the walk advances, so the "M-inactive-but-has-active-vikar" edge
            //     (R3a, Reviewer N1) fires here. If V's user is INACTIVE the vikar is
            //     SKIPPED (R3b) — no usable authority — and we fall through to M / escalation.
            var vikar = await _vikarRepo.GetActiveByApproverAsync(conn, primaryManagerId, effectiveAsOf, tx: null, ct);
            if (vikar is not null)
            {
                var vikarUserActive = await IsUserActiveAsync(conn, vikar.VikarUserId, ct);
                if (vikarUserActive)
                    return (vikar.VikarUserId, "ACTING_MANAGER", depth);
                // else: R3b — inactive stand-in, skip the vikar; fall through.
            }

            // 3. M if active.
            var primaryManagerActive = await IsUserActiveAsync(conn, primaryManagerId, ct);
            if (primaryManagerActive)
                return (primaryManagerId, "DESIGNATED_MANAGER", depth);

            // 4. Manager is inactive (and held no usable vikar) — walk up the chain.
            currentEmployeeId = primaryManagerId;
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
/// Thrown by <see cref="ReportingLineRepository.ValidateSameOrganisationAsync(string, string, CancellationToken)"/>
/// when the manager and employee belong to different Organisations (different
/// <c>primary_org_id</c> homes). The endpoint maps this to a 4xx (the assign callers return 400).
/// (S95 / ADR-035 slice 4: renamed from <c>CrossTreeAssignmentException</c> with the tree-WALK retirement.)
/// </summary>
public sealed class CrossOrganisationAssignmentException : Exception
{
    public CrossOrganisationAssignmentException(string message)
        : base(message) { }

    public CrossOrganisationAssignmentException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// S78 R9 — thrown by <see cref="ReportingLineRepository.AcquireTreeLockForEmployeeAsync"/> when the
/// employee's tree root, RE-DERIVED under the held advisory lock, DRIFTED from the pre-acquire
/// derivation: a concurrent cross-styrelse org-transfer (a <c>PUT /api/admin/users/{userId}</c>
/// changing <c>primary_org_id</c>) committed between the unlocked pre-acquire derive and the advisory
/// acquisition, so the advisory we hold is on the OLD (now stale) tree root.
///
/// <para>
/// <b>Why this MUST be a rollback-and-retry signal, not an in-tx re-acquire.</b>
/// <c>pg_advisory_xact_lock</c> releases ONLY at the end of the transaction (commit/rollback), so a
/// mid-tx "release the stale key + acquire the fresh key" is impossible; and RETAINING the stale key
/// while acquiring the fresh one invites lock-accumulation / an A↔B advisory deadlock. The ONLY sound
/// recovery is to ROLLBACK the whole transaction (it has taken no user-row <c>FOR UPDATE</c> and made
/// no mutation — the drift check runs BEFORE any of that) and RETRY the entire request body on a FRESH
/// transaction, re-deriving the root from scratch. Each caller wraps its handler in a BOUNDED retry
/// loop (≤3) and, on exhaustion, returns a PINNED 409 (never an incidental 5xx).
/// </para>
/// </summary>
public sealed class TreeRootDriftException : Exception
{
    /// <summary>The stale tree root we acquired the advisory on (the pre-acquire derivation).</summary>
    public string StaleTreeRoot { get; }

    /// <summary>The authoritative tree root re-derived under the held advisory (the new root).</summary>
    public string CurrentTreeRoot { get; }

    public TreeRootDriftException(string employeeId, string staleTreeRoot, string currentTreeRoot)
        : base(
            $"Tree root for employee '{employeeId}' drifted under the advisory lock: acquired on " +
            $"'{staleTreeRoot}' but re-derived as '{currentTreeRoot}' (a concurrent cross-styrelse " +
            "transfer committed between the unlocked derive and the advisory acquisition). " +
            "Roll back and retry on a fresh transaction.")
    {
        StaleTreeRoot = staleTreeRoot;
        CurrentTreeRoot = currentTreeRoot;
    }
}

/// <summary>
/// S74 R8 — thrown by <see cref="ReportingLineRepository.GuardNoCycleAsync"/> when an
/// assignment would create a reporting cycle: the chosen manager is the employee themselves,
/// or one of the employee's descendants. The endpoint maps this to a 4xx
/// (<c>409 Conflict</c>) — the assignment is well-formed but conflicts with the acyclic-tree
/// invariant (ADR-027).
/// </summary>
public sealed class ReportingCycleException : Exception
{
    public string EmployeeId { get; }
    public string ManagerId { get; }

    public ReportingCycleException(string employeeId, string managerId, string message)
        : base(message)
    {
        EmployeeId = employeeId;
        ManagerId = managerId;
    }
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
