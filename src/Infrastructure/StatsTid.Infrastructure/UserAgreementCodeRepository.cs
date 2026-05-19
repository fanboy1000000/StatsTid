using Npgsql;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S34 / TASK-3402 — Phase 4e versioned-history store for the per-user agreement-code
/// assignment (ADR-023 D2 option (b)). Mirrors the S29 <see cref="WageTypeMappingRepository"/>
/// / S33 <see cref="EmployeeProfileRepository"/> bitemporal shape: each row carries
/// <c>effective_from</c> + <c>effective_to</c> under end-exclusive <c>[from, to)</c>
/// semantics (ADR-018 D9) and a monotonic <c>version</c> column for ADR-019 admin-strict
/// If-Match optimistic concurrency. The partial-unique-index
/// <c>idx_user_agreement_codes_live</c> enforces "at most one live (open) row per user".
///
/// <para>
/// <b>Canonical-write contract.</b> All writes to <c>user_agreement_codes</c> MUST flow
/// through this repository. <c>users.agreement_code</c> is a denormalized cache for
/// live-only consumers (JWT mint via <see cref="GetCurrentAsync"/>, current-row reads by
/// Skema/Overtime/Compliance endpoints); the cache write happens in the same atomic tx
/// as the repository call (responsibility of the calling endpoint — TASK-3407 admin
/// PUT/POST). Past-period readers MUST route through
/// <see cref="GetByUserIdAtAsync"/> — never read <c>users.agreement_code</c> for
/// replay-sensitive paths (payroll export effective-date lookup, PCS planner snapshot
/// resolution). This contract is enforced by D-tests asserting cache-canonical agreement
/// after PUT (TASK-3414).
/// </para>
///
/// <para>
/// <b>Step 0b BLOCKER 1 absorption — no SoftDelete method.</b> Soft-delete is
/// semantically meaningless for <c>agreement_code</c>: every user must have an
/// agreement at all times; admins change it, never NULL it. The bitemporal
/// <c>effective_to</c> column stays in the schema (TASK-3401) for the Case C
/// predecessor-close path of <see cref="SupersedeAndCreateAsync"/>; the public
/// repository surface does not expose a soft-delete method.
/// </para>
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D5).</b> <see cref="SupersedeAndCreateAsync"/> is
/// a <c>(conn, tx)</c> overload — the endpoint owns the transaction, threading audit +
/// outbox writes into the same atomic unit. Read methods are self-managed.
/// </para>
/// </summary>
public sealed class UserAgreementCodeRepository
{
    private readonly DbConnectionFactory _dbFactory;

    public UserAgreementCodeRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ------------------------------------------------------------------
    // Reads — self-managed connections. Live + as-of + with-version flavors.
    // ------------------------------------------------------------------

    /// <summary>
    /// S34 / TASK-3402 — dated lookup of the agreement code in effect for
    /// <paramref name="userId"/> on <paramref name="asOfDate"/>. Returns the
    /// <c>agreement_code</c> of the row whose history window
    /// <c>[effective_from, effective_to)</c> contains <paramref name="asOfDate"/>, or
    /// <c>null</c> if no row covers that date for the user.
    ///
    /// <para>
    /// <b>End-exclusive predicate</b> per ADR-018 D9: a row with
    /// <c>effective_to = '2026-06-01'</c> covers <c>2026-05-31</c> but NOT
    /// <c>2026-06-01</c> — boundary days belong to the successor. Live rows
    /// (<c>effective_to IS NULL</c>) cover every day from <c>effective_from</c> forward.
    /// </para>
    ///
    /// <para>
    /// <b>This is the canonical past-period source.</b> Payroll export effective-date
    /// lookup (ADR-018 D14 export-time pattern) and PCS planner snapshot resolution
    /// MUST route through this method — never read <c>users.agreement_code</c> for
    /// replay-sensitive paths.
    /// </para>
    /// </summary>
    public async Task<string?> GetByUserIdAtAsync(
        string userId, DateOnly asOfDate, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT agreement_code
            FROM user_agreement_codes
            WHERE user_id = @userId
              AND effective_from <= @asOfDate
              AND (effective_to IS NULL OR effective_to > @asOfDate)
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    /// <summary>
    /// S34 / TASK-3402 — convenience read of the live row's <c>agreement_code</c> for
    /// <paramref name="userId"/>. Returns <c>null</c> when no live row exists.
    ///
    /// <para>
    /// Consumed by JWT mint (TASK-3406 sign-in path) and by live-only endpoint reads
    /// (Skema/Overtime/Compliance "today's" agreement). Equivalent to
    /// <see cref="GetByUserIdAtAsync"/> with <c>asOfDate = today</c> but reads the
    /// partial-unique-index <c>idx_user_agreement_codes_live</c> directly for a tighter
    /// query plan.
    /// </para>
    /// </summary>
    public async Task<string?> GetCurrentAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT agreement_code
            FROM user_agreement_codes
            WHERE user_id = @userId AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    /// <summary>
    /// S34 / TASK-3402 — atomic row + version read of the live row for
    /// <paramref name="userId"/>, used by admin GET handlers (TASK-3407) to stamp the
    /// ETag from the same snapshot whose data they serialize. Returns <c>null</c> when
    /// no live row exists.
    ///
    /// <para>
    /// Mirrors S33 <c>EmployeeProfileRepository.GetByEmployeeIdWithVersionAsync</c>
    /// (Step 7a P2 fix): reading agreement_code and version in separate statements
    /// opens a concurrency window where the response can carry stale data with a newer
    /// ETag and the next admin edit would silently overwrite the racing change.
    /// </para>
    /// </summary>
    public async Task<(string AgreementCode, long Version)?> GetCurrentWithVersionAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT agreement_code, version
            FROM user_agreement_codes
            WHERE user_id = @userId AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (reader.GetString(0), reader.GetInt64(1));
    }

    // ------------------------------------------------------------------
    // Writes — atomic-outbox (conn, tx) overload only (ADR-018 D5).
    // ADR-020 D2 3-case routing under SELECT ... FOR UPDATE.
    // ------------------------------------------------------------------

    /// <summary>
    /// S34 / TASK-3402 — ADR-020 D2 3-case routing under <c>SELECT ... FOR UPDATE</c>.
    /// Canonical write path for per-user agreement-code assignments.
    ///
    /// <para>
    /// <b>Routing</b> (decided after acquiring a row-level lock on the live row, if any,
    /// for <paramref name="req"/><c>.UserId</c> via <c>SELECT ... FOR UPDATE</c>):
    /// <list type="bullet">
    ///   <item><description><b>Case A — Created.</b> No live row exists. Allowed only when
    ///     <paramref name="expectedVersion"/> is <c>null</c> (seeder / admin-POST path).
    ///     INSERT a fresh row at <c>(effective_from = req.EffectiveFrom, effective_to = NULL,
    ///     version = 1)</c>. Returns <see cref="SaveUserAgreementCodeOutcome.Created"/>.</description></item>
    ///   <item><description><b>Case B — Updated.</b> Live row exists and its
    ///     <c>effective_from</c> equals <paramref name="req"/><c>.EffectiveFrom</c>.
    ///     UPDATE in-place: refresh <c>agreement_code</c>, bump <c>version = version + 1</c>,
    ///     stamp <c>updated_at = NOW()</c>; <c>assignment_id</c> and <c>effective_from</c>
    ///     are immutable. Returns <see cref="SaveUserAgreementCodeOutcome.Updated"/>.</description></item>
    ///   <item><description><b>Case C — Superseded.</b> Live row exists and its
    ///     <c>effective_from</c> is strictly earlier than <paramref name="req"/><c>.EffectiveFrom</c>.
    ///     Close the predecessor by stamping <c>effective_to = req.EffectiveFrom</c>
    ///     (end-exclusive per ADR-018 D9 — predecessor's history window becomes
    ///     <c>[predecessor.effective_from, req.EffectiveFrom)</c>; <b>version unchanged</b>),
    ///     then INSERT a new live row at
    ///     <c>(effective_from = req.EffectiveFrom, effective_to = NULL,
    ///     version = predecessor.Version + 1)</c> per S33 Step 7a P1 ETag-monotonicity
    ///     refinement (successor inherits predecessor.Version + 1 because the natural key
    ///     is a single column <c>user_id</c>, unlike WTM's composite key — the version
    ///     must carry the monotonic load alone). Returns
    ///     <see cref="SaveUserAgreementCodeOutcome.Superseded"/>.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Optimistic concurrency (ADR-019 admin-strict If-Match).</b>
    /// When <paramref name="expectedVersion"/> is <b>non-null</b>:
    /// <list type="bullet">
    ///   <item><description>No live row → throws <see cref="OptimisticConcurrencyException"/>
    ///     with <c>ActualVersion = null</c> (caller asserted a current state that does not
    ///     exist; degenerate mismatch).</description></item>
    ///   <item><description>Live row, version differs → throws
    ///     <see cref="OptimisticConcurrencyException"/> with the actual stored version.</description></item>
    /// </list>
    /// When <paramref name="expectedVersion"/> is <b>null</b> (seeder + admin-POST), no
    /// version check is performed; Case A is allowed and Cases B/C proceed unguarded.
    /// </para>
    ///
    /// <para>
    /// <b>Backdate guard.</b> When <paramref name="req"/><c>.EffectiveFrom &lt; predecessor.EffectiveFrom</c>,
    /// throws <see cref="InvalidProfileSupersessionException"/> (reused from the
    /// S22 LocalAgreementProfileRepository definition; mirrors S29 WTM + S33
    /// EmployeeProfile precedent — exception name predates Phase 4e specialization).
    /// </para>
    ///
    /// <para>
    /// <b>Atomic-outbox contract (ADR-018 D5).</b> Caller owns the transaction; this
    /// method only writes to <c>user_agreement_codes</c>. The endpoint (TASK-3407) emits
    /// the audit row + outbox event in the same tx after this returns, sourcing the event
    /// type from <see cref="SaveUserAgreementCodeResult.Outcome"/>.
    /// </para>
    /// </summary>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when <paramref name="expectedVersion"/> is non-null and (a) no live row
    /// exists or (b) the live row's <c>version</c> column differs from
    /// <paramref name="expectedVersion"/>. Endpoint maps to 412 per ADR-019.
    /// </exception>
    /// <exception cref="InvalidProfileSupersessionException">
    /// Thrown when <paramref name="req"/><c>.EffectiveFrom</c> is strictly earlier than
    /// the predecessor's <c>effective_from</c> (backdate rejected per ADR-018 D9 strict-less
    /// under end-exclusive). Endpoint maps to 400/422.
    /// </exception>
    public async Task<SaveUserAgreementCodeResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        UserAgreementCodeSupersedeRequest req, long? expectedVersion,
        CancellationToken ct = default)
    {
        // 1. SELECT ... FOR UPDATE the live row (if any). The partial-unique-index
        //    `idx_user_agreement_codes_live` guarantees at most one matching row; the
        //    row-level lock serializes concurrent writers attempting to supersede or
        //    update the same user's live assignment. Mirrors S33 EmployeeProfile
        //    precedent at EmployeeProfileRepository.AcquireLockAsync.
        var predecessorNullable = await AcquireLockAsync(conn, tx, req.UserId, ct);

        // 2. Case A — no live row.
        if (predecessorNullable is null)
        {
            if (expectedVersion is not null)
            {
                // Caller asserted a current version, but there is no live row →
                // degenerate mismatch (412). ActualVersion = null distinguishes this
                // branch from the "live row exists, version differs" branch below.
                throw new OptimisticConcurrencyException(
                    $"No live agreement-code assignment exists for user_id='{req.UserId}', " +
                    $"but caller sent If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            // Case A: no predecessor → version=1 baseline. S35 / TASK-3502 — catch
            // PostgresException SqlState 23505 from the partial-unique-index
            // `idx_user_agreement_codes_live` and re-throw as the typed
            // ConcurrentSeedConflictException so the endpoint can map it to 409
            // Conflict (symmetric to OptimisticConcurrencyException → 412 above).
            // The race: two concurrent Case A callers both observe "no live row"
            // before the partial-unique-index serializes their INSERTs; the loser
            // raises 23505. Only Case A can hit this — Case B updates the locked
            // row in-place (no INSERT) and Case C inserts at a new effective_from
            // AFTER closing the predecessor (effective_to non-NULL on predecessor,
            // so the partial-unique-index admits the new row by construction).
            try
            {
                var (newAssignmentId, newVersion) =
                    await InsertLiveRowAsync(conn, tx, req, nextVersion: 1L, ct);
                return new SaveUserAgreementCodeResult(
                    newAssignmentId, newVersion, SaveUserAgreementCodeOutcome.Created);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                throw new ConcurrentSeedConflictException(req.UserId);
            }
        }

        // Hoist out of the nullable tuple now that we've eliminated the null branch.
        var predecessor = predecessorNullable.Value;

        // 3. Predecessor exists. Validate optimistic concurrency (when If-Match supplied).
        if (expectedVersion is not null && predecessor.Version != expectedVersion.Value)
        {
            throw new OptimisticConcurrencyException(
                $"User agreement-code assignment version is {predecessor.Version}, but caller sent " +
                $"If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: predecessor.Version);
        }

        // 4. Backdate guard (ADR-018 D9 strict-less under end-exclusive). A new row
        //    cannot start before its predecessor — there is no valid history window
        //    for the predecessor in that case. Mirrors S29 WTM + S33 EmployeeProfile
        //    precedent.
        if (req.EffectiveFrom < predecessor.EffectiveFrom)
        {
            throw new InvalidProfileSupersessionException(
                $"Cannot supersede user agreement-code assignment for user_id='{req.UserId}' " +
                $"with effective_from {req.EffectiveFrom:yyyy-MM-dd} earlier than " +
                $"predecessor's effective_from {predecessor.EffectiveFrom:yyyy-MM-dd}.");
        }

        // 5. Case B — same-day edit. UPDATE-in-place with version bump.
        if (req.EffectiveFrom == predecessor.EffectiveFrom)
        {
            var (sameDayAssignmentId, sameDayVersion) =
                await UpdateInPlaceAsync(conn, tx, req, predecessor.AssignmentId, ct);
            return new SaveUserAgreementCodeResult(
                sameDayAssignmentId, sameDayVersion, SaveUserAgreementCodeOutcome.Updated);
        }

        // 6. Case C — cross-day edit. Close the predecessor at end-exclusive
        //    `effective_to = req.EffectiveFrom` (version UNCHANGED — close is lifecycle,
        //    not a content edit), then INSERT new live row at predecessor.Version + 1
        //    per S33 Step 7a P1 ETag-monotonicity refinement (single-column natural key
        //    forces the version to carry the monotonic load alone — see InsertLiveRowAsync
        //    xmldoc).
        await ClosePredecessorAsync(conn, tx, predecessor.AssignmentId, req.EffectiveFrom, ct);
        var (supersedingAssignmentId, supersedingVersion) =
            await InsertLiveRowAsync(conn, tx, req, nextVersion: predecessor.Version + 1, ct);
        return new SaveUserAgreementCodeResult(
            supersedingAssignmentId, supersedingVersion, SaveUserAgreementCodeOutcome.Superseded);
    }

    // ------------------------------------------------------------------
    // Private helpers — shared by SupersedeAndCreateAsync's three routing branches.
    // Mirrors S33 EmployeeProfileRepository's AcquireLockAsync / InsertLiveRowAsync /
    // UpdateInPlaceAsync / ClosePredecessorAsync triad.
    // ------------------------------------------------------------------

    /// <summary>
    /// Locks the live row (<c>effective_to IS NULL</c>) for <paramref name="userId"/> via
    /// <c>SELECT ... FOR UPDATE</c>. Returns the locked row's <c>assignment_id</c>,
    /// current <c>version</c>, and <c>effective_from</c> — the three pieces of state
    /// <see cref="SupersedeAndCreateAsync"/> needs to route Cases A/B/C and validate
    /// optimistic concurrency. Returns <c>null</c> when no live row exists (Case A).
    /// </summary>
    private static async Task<(Guid AssignmentId, long Version, DateOnly EffectiveFrom)?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string userId, CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT assignment_id, version, effective_from
            FROM user_agreement_codes
            WHERE user_id = @userId
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (
            reader.GetGuid(0),
            reader.GetInt64(1),
            reader.GetFieldValue<DateOnly>(2));
    }

    /// <summary>
    /// Case A (Create) + Case C (Supersede) shared path — INSERT a fresh live row with
    /// the caller-supplied <c>effective_from</c>. <c>assignment_id</c> is generated
    /// client-side (S29 WTM + S33 EmployeeProfile precedent) so the endpoint can include
    /// it in the outbox event body. The partial-unique-index
    /// <c>idx_user_agreement_codes_live</c> guarantees at most one open row per user;
    /// in Case C the caller has already closed the predecessor under the same tx.
    ///
    /// <para>
    /// <b>Version contract (S33 Step 7a P1 absorption — ETag monotonicity fix).</b>
    /// Case A passes <paramref name="nextVersion"/> = 1 (no predecessor exists).
    /// Case C passes <paramref name="nextVersion"/> = <c>predecessor.Version + 1</c>
    /// so the admin's response ETag strictly increases across the supersession.
    /// Without this, a legacy/seeder-backfilled assignment at version=1 superseded
    /// across days would yield a new live row also at version=1, and a racing admin
    /// holding old <c>If-Match: "1"</c> could overwrite the newly superseded row without
    /// a 412 — ADR-019 D2 contract violation. The bump-on-Case-C diverges from
    /// ADR-020 D2's literal "version=1 for new row" wording but inherits the SPIRIT of
    /// D2 (each successor is a fresh logical row); the WTM precedent doesn't suffer
    /// this because WTM's natural key includes effective_from, making (key, version)
    /// globally unique — UserAgreementCode's natural key is just <c>user_id</c>, so
    /// the version must carry the monotonic load alone.
    /// </para>
    /// </summary>
    private static async Task<(Guid AssignmentId, long Version)> InsertLiveRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        UserAgreementCodeSupersedeRequest req, long nextVersion, CancellationToken ct)
    {
        var newAssignmentId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (
                assignment_id, user_id, agreement_code,
                effective_from, effective_to, version)
            VALUES (
                @assignmentId, @userId, @agreementCode,
                @effectiveFrom, NULL, @version)
            RETURNING assignment_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("assignmentId", newAssignmentId);
        cmd.Parameters.AddWithValue("userId", req.UserId);
        cmd.Parameters.AddWithValue("agreementCode", req.AgreementCode);
        cmd.Parameters.AddWithValue("effectiveFrom", req.EffectiveFrom);
        cmd.Parameters.AddWithValue("version", nextVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING always yields one row on success.
            throw new InvalidOperationException(
                $"InsertLiveRowAsync produced no row for user_id='{req.UserId}' " +
                $"at effective_from='{req.EffectiveFrom:yyyy-MM-dd}'.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case B (Updated) — same-day UPDATE-in-place. Targets the (still-locked) live row
    /// by its <paramref name="assignmentId"/>; refreshes <c>agreement_code</c>, bumps
    /// <c>version = version + 1</c>, stamps <c>updated_at = NOW()</c>;
    /// <c>assignment_id</c> and <c>effective_from</c> are immutable across same-day
    /// edits.
    /// </summary>
    private static async Task<(Guid AssignmentId, long Version)> UpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        UserAgreementCodeSupersedeRequest req, Guid assignmentId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE user_agreement_codes SET
                agreement_code = @agreementCode,
                version = version + 1,
                updated_at = NOW()
            WHERE assignment_id = @assignmentId
            RETURNING assignment_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("assignmentId", assignmentId);
        cmd.Parameters.AddWithValue("agreementCode", req.AgreementCode);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"UpdateInPlaceAsync produced no row for assignment_id='{assignmentId}'; " +
                "FOR UPDATE invariant violated.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case C (Supersede) — close the predecessor by stamping
    /// <c>effective_to = closeDate</c> under end-exclusive semantics (ADR-018 D9 —
    /// predecessor's history window becomes <c>[predecessor.effective_from, closeDate)</c>).
    /// The version column is NOT bumped: close is a lifecycle event, not a content edit
    /// (mirrors S22 ArchiveProfileAsync + S29 WTM CloseRowAsync + S33 EmployeeProfile
    /// ClosePredecessorAsync). Caller must already hold the row lock acquired via
    /// <see cref="AcquireLockAsync"/>.
    /// </summary>
    private static async Task ClosePredecessorAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid assignmentId, DateOnly closeDate, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            "UPDATE user_agreement_codes SET effective_to = @closeDate WHERE assignment_id = @assignmentId",
            conn, tx);
        closeCmd.Parameters.AddWithValue("closeDate", closeDate);
        closeCmd.Parameters.AddWithValue("assignmentId", assignmentId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }
}

// ------------------------------------------------------------------
// Request + result records — colocated with the repository per S33 EmployeeProfile +
// S29 WTM precedent.
// ------------------------------------------------------------------

/// <summary>
/// S34 / TASK-3402 — payload for
/// <see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/>. Drives ADR-020 D2
/// 3-case routing via the explicit <see cref="EffectiveFrom"/> date (same-day vs
/// cross-day vs net-new). The endpoint reads the clock (no clock dependency in the repo);
/// seeders + admin-POST + admin-PUT supply the date directly.
/// </summary>
public sealed record UserAgreementCodeSupersedeRequest(
    string UserId,
    string AgreementCode,
    DateOnly EffectiveFrom);

/// <summary>
/// S34 / TASK-3402 — result of
/// <see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/>.
/// <see cref="Outcome"/> discriminates which of the ADR-020 D2 3-case branches fired so
/// the endpoint (TASK-3407) can emit the correct event type —
/// <c>UserAgreementCodeAssigned</c>, <c>UserAgreementCodeUpdated</c>, or
/// <c>UserAgreementCodeSuperseded</c> — and stamp the right audit <c>action</c> column
/// (CREATED / UPDATED / SUPERSEDED).
/// </summary>
/// <param name="AssignmentId">The <c>assignment_id</c> of the row this call produced. In
/// Case A (Created) and Case C (Superseded) this is a freshly-generated UUID for the new
/// live row; in Case B (Updated) it is the predecessor's unchanged
/// <c>assignment_id</c>.</param>
/// <param name="Version">The post-write <c>version</c> column value on the row identified
/// by <see cref="AssignmentId"/>. Case A → 1; Case B → <c>prior + 1</c>; Case C →
/// <c>predecessor.Version + 1</c> per S33 Step 7a P1 ETag-monotonicity refinement.</param>
/// <param name="Outcome">Which ADR-020 D2 branch the call routed through.</param>
public sealed record SaveUserAgreementCodeResult(
    Guid AssignmentId,
    long Version,
    SaveUserAgreementCodeOutcome Outcome);

/// <summary>
/// S34 / TASK-3402 — ADR-020 D2 3-case routing discriminator. Read by TASK-3407 endpoint
/// to map each case to its correct outbox event type:
/// <list type="bullet">
///   <item><description><see cref="Created"/> → <c>UserAgreementCodeAssigned</c>
///     (net-new live row; no predecessor existed).</description></item>
///   <item><description><see cref="Updated"/> → <c>UserAgreementCodeUpdated</c>
///     (same-day in-place edit; predecessor's <c>effective_from</c> matched the
///     request's, version bumped).</description></item>
///   <item><description><see cref="Superseded"/> → <c>UserAgreementCodeSuperseded</c>
///     (cross-day supersession; predecessor closed at end-exclusive
///     <c>effective_to</c>, new live row at <c>predecessor.Version + 1</c>).</description></item>
/// </list>
/// </summary>
public enum SaveUserAgreementCodeOutcome
{
    /// <summary>Case A — no live row existed; INSERT produced a brand-new live
    /// assignment row.</summary>
    Created,
    /// <summary>Case B — live row existed and its <c>effective_from</c> matched the
    /// request's; UPDATE-in-place with version bump (<c>assignment_id</c> and
    /// <c>effective_from</c> unchanged).</summary>
    Updated,
    /// <summary>Case C — live row existed at an earlier <c>effective_from</c>;
    /// predecessor closed at end-exclusive
    /// <c>effective_to = request.EffectiveFrom</c> (version unchanged), new live row
    /// inserted at <c>predecessor.Version + 1</c>.</summary>
    Superseded,
}
