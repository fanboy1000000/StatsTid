using System.Data;
using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Repository for <see cref="LocalAgreementProfile"/> rows in <c>local_agreement_profiles</c>.
///
/// Per ADR-017 D2 / D2.1 + ADR-018 D7-D10: lifecycle is <c>effective_to</c>-only (no
/// <c>is_active</c>). At most one open-ended profile per (org_id, agreement_code, ok_version)
/// is enforced at the schema level by the partial-unique-index <c>uq_local_agreement_profile_active</c>
/// WHERE effective_to IS NULL. Save runs inside a single
/// <see cref="IsolationLevel.RepeatableRead"/> transaction with <c>SELECT ... FOR UPDATE</c>
/// on the current open row to gate concurrent writers.
///
/// Routing (ADR-018 D9):
/// <list type="bullet">
/// <item><description>No predecessor → first-create insert (version 1).</description></item>
/// <item><description><c>newProfile.EffectiveFrom == predecessor.EffectiveFrom</c> →
/// UPDATE-in-place. <c>profile_id</c> stable, version bumped by one. Audit-action MODIFIED
/// at the call site.</description></item>
/// <item><description><c>newProfile.EffectiveFrom &gt; predecessor.EffectiveFrom</c> →
/// supersession (close-then-insert). Predecessor closes at end-exclusive
/// <c>effective_to = newProfile.EffectiveFrom</c> per ADR-018 D8 (no <c>-1</c>); new row
/// inserts at version 1. Audit-action SUPERSEDED at the call site.</description></item>
/// <item><description><c>newProfile.EffectiveFrom &lt; predecessor.EffectiveFrom</c> →
/// rejected with <see cref="InvalidProfileSupersessionException"/> (ADR-018 D9 backdate
/// guard, strict-less under end-exclusive). The PUT handler maps this to 400.</description></item>
/// </list>
///
/// Optimistic-concurrency token (ADR-018 D7): the row's <c>version BIGINT</c> column is the
/// ETag. <c>If-Match: "&lt;version&gt;"</c> on the wire (RFC 7232 quoted) carries the
/// numeric version into <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>'s
/// <c>expectedCurrentVersion</c> parameter.
///
/// Pattern follows <see cref="LocalConfigurationRepository"/>: read methods open their own
/// connection via the injected <see cref="DbConnectionFactory"/>. Writes have two flavors:
/// <list type="bullet">
/// <item><description>Self-contained:
/// <see cref="SupersedeAndCreateAsync(System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// owns its own connection and transaction. Returns <c>(profileId, version)</c>.</description></item>
/// <item><description>In-transaction sibling:
/// <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// reuses a caller-supplied connection + transaction so the caller can extend the same
/// PostgreSQL transaction across outbox + audit-row writes (ADR-018 D3 transactional
/// outbox contract — no two-phase commit across stores; same database).</description></item>
/// </list>
///
/// This repository is a pure CRUD facade. It does NOT enqueue outbox events or write audit
/// rows; the calling service (PUT endpoint, TASK-2205) is responsible for coordinating
/// outbox + audit writes alongside the profile mutation.
/// </summary>
public sealed class LocalAgreementProfileRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public LocalAgreementProfileRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Returns the at-most-one currently-active profile for the (org, agreement_code, ok_version)
    /// triple, or <c>null</c> if no profile is currently active. The partial-unique-index
    /// invariant (ADR-017 D1) guarantees zero or one match.
    /// </summary>
    public async Task<LocalAgreementProfile?> GetCurrentOpenAsync(
        string orgId, string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM local_agreement_profiles
            WHERE org_id = @orgId
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// Returns profile activations whose <c>effective_from</c> falls strictly inside
    /// (<paramref name="periodStart"/>, <paramref name="periodEnd"/>] AND that overlap the
    /// period itself (closed predecessors that closed before <paramref name="periodStart"/>
    /// are excluded). Ordered by <c>effective_from</c> ASC so callers see boundaries
    /// chronologically.
    ///
    /// Used by the segmentation hydration shim (ADR-017 D9c) to feed
    /// <see cref="StatsTid.SharedKernel.Segmentation.BoundarySources.LocalProfileActivations"/>.
    /// The strict-greater inequality on <paramref name="periodStart"/> reflects the segment-1
    /// invariant: the profile active on <paramref name="periodStart"/> is the starting state,
    /// not an interior boundary.
    ///
    /// End-exclusive <c>effective_to</c> predicate (ADR-018 D8 + D10): the overlap test is
    /// <c>effective_to &gt; @periodStart</c> (strict-greater), NOT <c>&gt;=</c>. Under
    /// end-exclusive semantics a row with <c>effective_to = periodStart</c> ended *before*
    /// <c>periodStart</c> begins and therefore does NOT overlap the period.
    /// </summary>
    public async Task<IReadOnlyList<(DateOnly EffectiveFrom, Guid ProfileId)>> GetActivationsInPeriodAsync(
        string orgId, string agreementCode, string okVersion,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_from, profile_id FROM local_agreement_profiles
            WHERE org_id = @orgId
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_from > @periodStart
              AND effective_from <= @periodEnd
              AND (effective_to IS NULL OR effective_to > @periodStart)
            ORDER BY effective_from ASC
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("periodStart", periodStart);
        cmd.Parameters.AddWithValue("periodEnd", periodEnd);
        var results = new List<(DateOnly, Guid)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var effectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(0));
            var profileId = reader.GetGuid(1);
            results.Add((effectiveFrom, profileId));
        }
        return results;
    }

    /// <summary>
    /// Returns closed predecessor profiles for the (org, agreement_code, ok_version) triple,
    /// ordered most-recently-closed first. The currently-active profile (effective_to IS NULL)
    /// is intentionally excluded — callers fetch it via <see cref="GetCurrentOpenAsync"/> and
    /// merge if a combined view is needed.
    /// </summary>
    public async Task<IReadOnlyList<LocalAgreementProfile>> GetHistoryAsync(
        string orgId, string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM local_agreement_profiles
            WHERE org_id = @orgId
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NOT NULL
            ORDER BY effective_from DESC
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        var profiles = new List<LocalAgreementProfile>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            profiles.Add(MapReader(reader));
        return profiles;
    }

    /// <summary>
    /// Atomically routes <paramref name="newProfile"/> through ADR-018 D9's three branches —
    /// first-create, UPDATE-in-place, or close-then-insert supersession — based on the
    /// relationship between <c>newProfile.EffectiveFrom</c> and the predecessor's
    /// <c>effective_from</c>. Self-contained overload: opens its own connection and transaction.
    ///
    /// For cross-store atomicity (outbox + audit + profile in a single transaction —
    /// ADR-018 D3 transactional outbox contract), use the in-transaction sibling overload
    /// <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>.
    ///
    /// Concurrency contract (ADR-018 D7):
    /// <list type="bullet">
    /// <item><description><paramref name="expectedCurrentVersion"/> = <c>null</c> means the
    /// caller asserted "no current profile exists" (HTTP <c>If-None-Match: *</c> for first
    /// creation). If the lock SELECT finds a row, <see cref="OptimisticConcurrencyException"/>
    /// is thrown with <see cref="OptimisticConcurrencyException.ActualVersion"/> populated.</description></item>
    /// <item><description><paramref name="expectedCurrentVersion"/> non-null means the caller
    /// asserted that version is current (HTTP <c>If-Match: "&lt;version&gt;"</c>). The lock
    /// SELECT must return a row whose version matches; mismatch (including null = "now no
    /// current open") throws <see cref="OptimisticConcurrencyException"/>.</description></item>
    /// </list>
    ///
    /// Transaction: <see cref="IsolationLevel.RepeatableRead"/>. The
    /// <c>SELECT ... FOR UPDATE</c> on the partial-unique row serializes concurrent writers to
    /// the same (org, agreement_code, ok_version) triple. PostgreSQL RepeatableRead is
    /// sufficient here — it prevents non-repeatable reads and the row-lock blocks competitors.
    /// </summary>
    /// <returns>The post-write profile id and version (and whether the same-day write was
    /// a no-op — <see cref="SaveProfileResult.IsNoOp"/>). For first-create + supersession the
    /// version is <c>1</c>; for UPDATE-in-place it is <c>predecessor.Version + 1</c> unless
    /// the candidate's overridable fields all match the predecessor (S23 / TASK-2304 same-day
    /// no-op short-circuit), in which case the predecessor's version is returned unchanged
    /// and the caller skips audit + outbox emission.</returns>
    /// <exception cref="OptimisticConcurrencyException">If the precondition encoded in
    /// <paramref name="expectedCurrentVersion"/> does not match the row currently holding the
    /// active slot.</exception>
    /// <exception cref="InvalidProfileSupersessionException">If
    /// <c>newProfile.EffectiveFrom &lt; predecessor.EffectiveFrom</c>.</exception>
    public async Task<SaveProfileResult> SupersedeAndCreateAsync(
        long? expectedCurrentVersion, LocalAgreementProfile newProfile, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            var result = await SupersedeAndCreateAsync(conn, tx, expectedCurrentVersion, newProfile, ct);
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
    /// In-transaction sibling overload of
    /// <see cref="SupersedeAndCreateAsync(System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> and <paramref name="tx"/> so the
    /// caller can extend the same PostgreSQL transaction across outbox + audit-row writes
    /// (ADR-018 D3 transactional outbox contract). The caller is responsible for committing
    /// or rolling back the transaction; this method does NOT commit and does NOT rollback.
    ///
    /// Concurrency semantics, isolation expectation, and exception contract are identical to
    /// the self-contained overload — the caller should open the transaction with
    /// <see cref="IsolationLevel.RepeatableRead"/> for equivalent guarantees.
    /// </summary>
    /// <returns>The post-write profile id, version, and a no-op flag. See <see cref="SaveProfileResult"/>
    /// for semantics. For first-create + supersession the version is <c>1</c>; for UPDATE-in-place
    /// it is <c>predecessor.Version + 1</c>; for the S23 same-day no-op path it is
    /// <c>predecessor.Version</c> (unchanged) with <see cref="SaveProfileResult.IsNoOp"/> = true.</returns>
    /// <exception cref="OptimisticConcurrencyException">If the precondition encoded in
    /// <paramref name="expectedCurrentVersion"/> does not match the row currently holding the
    /// active slot.</exception>
    /// <exception cref="InvalidProfileSupersessionException">If
    /// <c>newProfile.EffectiveFrom &lt; predecessor.EffectiveFrom</c>.</exception>
    public async Task<SaveProfileResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long? expectedCurrentVersion,
        LocalAgreementProfile newProfile,
        CancellationToken ct = default)
    {
        // 1. Lock the currently-active row (if any) so concurrent supersessions serialize
        //    here. Returns (profile_id, version, effective_from) so we can both verify the
        //    optimistic-concurrency token AND route same-day vs supersession.
        var current = await AcquireLockAsync(
            conn, tx, newProfile.OrgId, newProfile.AgreementCode, newProfile.OkVersion, ct);

        // 2. Validate the precondition (If-None-Match: * vs If-Match: "<version>").
        //    LOAD-BEARING: Codex S23 Step 0b BLOCKER on the original endpoint-level no-op
        //    fast path was that it bypassed THIS check, letting stale callers get 200 when
        //    the stored version had advanced. The no-op short-circuit at 3b-noop runs AFTER
        //    this check, NEVER before it.
        ValidatePrecondition(current?.Version, expectedCurrentVersion);

        if (current is { } predecessor)
        {
            // 3a. Backdate guard (ADR-018 D9 strict-less under end-exclusive). A new
            //     profile cannot start before its predecessor — there is no valid history
            //     window for the predecessor in that case. The PUT handler maps this to 400.
            if (newProfile.EffectiveFrom < predecessor.EffectiveFrom)
            {
                throw new InvalidProfileSupersessionException(
                    $"Cannot supersede with effective_from {newProfile.EffectiveFrom:yyyy-MM-dd} " +
                    $"earlier than predecessor's effective_from {predecessor.EffectiveFrom:yyyy-MM-dd}.");
            }

            // 3b. Same-day save (ADR-018 D9 MODIFIED branch).
            if (newProfile.EffectiveFrom == predecessor.EffectiveFrom)
            {
                // 3b-noop. S23 / TASK-2304 — short-circuit when no overridable field changed.
                //     Detection happens AFTER lock + ValidatePrecondition (Codex Step 0b
                //     BLOCKER fix): a stale caller cannot reach this path because their
                //     If-Match is rejected by ValidatePrecondition first. If we reach here
                //     and all 5 overridable fields match the predecessor, do NOT bump
                //     version, do NOT UPDATE the row. Return predecessor.Version unchanged
                //     with IsNoOp = true; the PUT handler skips audit + outbox emission.
                if (AllOverridableFieldsMatch(predecessor, newProfile))
                {
                    return new SaveProfileResult(predecessor.ProfileId, predecessor.Version, IsNoOp: true);
                }

                // 3b-update. UPDATE-in-place. profile_id stable; version bumped by one;
                //     effective_from / effective_to immutable. The audit-action at the call
                //     site is MODIFIED (vs SUPERSEDED for 3c below).
                var newVersion = await UpdateInPlaceAsync(
                    conn, tx, predecessor.ProfileId, newProfile, predecessor.Version, ct);
                return new SaveProfileResult(predecessor.ProfileId, newVersion, IsNoOp: false);
            }

            // 3c. Supersession (newProfile.EffectiveFrom > predecessor.EffectiveFrom):
            //     close predecessor at end-exclusive newProfile.EffectiveFrom (ADR-018 D8
            //     — NO -1 day shift; the predecessor's history window is
            //     [predecessor.EffectiveFrom, newProfile.EffectiveFrom)). Audit-action at
            //     the call site is SUPERSEDED.
            await CloseProfileAsync(conn, tx, predecessor.ProfileId, newProfile.EffectiveFrom, ct);
        }

        // 4. Insert the new currently-active profile at version 1. ProfileId is generated
        //    client-side (matches LocalConfigurationRepository.CreateAsync precedent).
        var newProfileId = newProfile.ProfileId == Guid.Empty ? Guid.NewGuid() : newProfile.ProfileId;
        try
        {
            await InsertProfileAsync(conn, tx, newProfile, newProfileId, version: 1, ct);
        }
        catch (PostgresException ex) when (
            ex.SqlState == "23505" &&
            ex.ConstraintName == "uq_local_agreement_profile_active")
        {
            // Step-7a cycle-1 (S21) preserved: the empty-slot path takes no row lock
            // (PostgreSQL cannot SELECT ... FOR UPDATE a non-existent row), so two
            // concurrent If-None-Match: * requests can both pass ValidatePrecondition and
            // collide in the INSERT. The partial-unique-index uq_local_agreement_profile_active
            // rejects the loser with SqlState 23505. Translate that into the contract shape
            // the PUT handler expects (OptimisticConcurrencyException → 412 Precondition
            // Failed) so the caller does not see a raw 500. Under ADR-018 D7 the actual
            // version is unknown to the loser (it didn't read the winner's row), so we
            // surface actualVersion: null and let the PUT handler refresh-and-retry.
            throw new OptimisticConcurrencyException(
                "Another profile was created concurrently for the same " +
                "(org_id, agreement_code, ok_version) triple; refresh and retry.",
                expectedVersion: expectedCurrentVersion,
                actualVersion: null,
                innerException: ex);
        }
        return new SaveProfileResult(newProfileId, Version: 1, IsNoOp: false);
    }

    /// <summary>
    /// Returns true when all 5 admin-overridable fields on <paramref name="predecessor"/>
    /// equal the corresponding values on <paramref name="candidate"/>. Used by the S23
    /// same-day no-op short-circuit to skip the UPDATE + audit + outbox emission when an
    /// admin saves a profile with no field changes (typical "open the form, change nothing,
    /// hit Save" admin flow).
    ///
    /// <para>
    /// The 5 fields are the entire whitelist of overridable columns on
    /// <c>local_agreement_profiles</c> per ADR-017 D9a (the schema columns ARE the
    /// whitelist; protected keys are absent from the schema by design):
    /// <c>WeeklyNormHours</c>, <c>MaxFlexBalance</c>, <c>FlexCarryoverMax</c>,
    /// <c>MaxOvertimeHoursPerPeriod</c>, <c>OvertimeRequiresPreApproval</c>. C# nullable
    /// equality (<c>==</c>) handles the (null, null) ⇒ true case correctly for both
    /// <c>decimal?</c> and <c>bool?</c>. Identity (<c>profile_id</c>, <c>effective_from</c>,
    /// <c>created_by</c>, <c>created_at</c>, <c>version</c>) and lifecycle (<c>effective_to</c>)
    /// columns are immutable across in-place edits and therefore irrelevant to this check.
    /// </para>
    /// </summary>
    private static bool AllOverridableFieldsMatch(
        LocalAgreementProfile predecessor, LocalAgreementProfile candidate)
    {
        return predecessor.WeeklyNormHours == candidate.WeeklyNormHours
            && predecessor.MaxFlexBalance == candidate.MaxFlexBalance
            && predecessor.FlexCarryoverMax == candidate.FlexCarryoverMax
            && predecessor.MaxOvertimeHoursPerPeriod == candidate.MaxOvertimeHoursPerPeriod
            && predecessor.OvertimeRequiresPreApproval == candidate.OvertimeRequiresPreApproval;
    }

    /// <summary>
    /// Acquires a row-level lock on the currently-active profile (if any) for the
    /// (org, agreement_code, ok_version) triple via <c>SELECT ... FOR UPDATE</c>. Returns
    /// the locked row as a full <see cref="LocalAgreementProfile"/>, or <c>null</c> if no
    /// profile is currently active. Concurrent writers attempting the same lock serialize
    /// on this query.
    ///
    /// <para>
    /// Returning the full profile (rather than just identity columns) lets
    /// <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
    /// inspect the overridable-field set during the no-op short-circuit (S23 / TASK-2304)
    /// without a second SELECT. The <c>version</c> column is the optimistic-concurrency
    /// token; the <c>effective_from</c> column drives the first-create / UPDATE-in-place /
    /// supersession routing.
    /// </para>
    /// </summary>
    private static async Task<LocalAgreementProfile?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string orgId, string agreementCode, string okVersion,
        CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT * FROM local_agreement_profiles
            WHERE org_id = @orgId
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("orgId", orgId);
        lockCmd.Parameters.AddWithValue("agreementCode", agreementCode);
        lockCmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapReader(reader) : null;
    }

    /// <summary>
    /// Applies the optimistic-concurrency precondition (HTTP If-Match / If-None-Match)
    /// against the version of the row actually present at lock time. Throws
    /// <see cref="OptimisticConcurrencyException"/> on mismatch with both the expected and
    /// actual versions surfaced in the exception (the PUT endpoint maps these to the 412
    /// body).
    /// </summary>
    private static void ValidatePrecondition(long? actualCurrentVersion, long? expectedCurrentVersion)
    {
        if (expectedCurrentVersion is null && actualCurrentVersion is not null)
        {
            throw new OptimisticConcurrencyException(
                $"Cannot create: a current profile already exists at version {actualCurrentVersion.Value}; " +
                $"use If-Match: \"{actualCurrentVersion.Value}\" for supersession.",
                expectedVersion: null,
                actualVersion: actualCurrentVersion);
        }
        if (expectedCurrentVersion is not null && actualCurrentVersion != expectedCurrentVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Current profile version is {actualCurrentVersion?.ToString() ?? "<none>"}, " +
                $"but caller sent If-Match: \"{expectedCurrentVersion.Value}\"; refresh and retry.",
                expectedVersion: expectedCurrentVersion,
                actualVersion: actualCurrentVersion);
        }
    }

    /// <summary>
    /// Closes the supplied currently-active profile by stamping <c>effective_to</c> to
    /// <paramref name="effectiveTo"/> under end-exclusive semantics (ADR-018 D8). The caller
    /// passes the new profile's <c>effective_from</c> directly — the predecessor's history
    /// window becomes <c>[predecessor.effective_from, effectiveTo)</c>. Caller must already
    /// hold the row lock acquired via <see cref="AcquireLockAsync"/>.
    /// </summary>
    private static async Task CloseProfileAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid currentProfileId, DateOnly effectiveTo, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            "UPDATE local_agreement_profiles SET effective_to = @effectiveTo WHERE profile_id = @currentProfileId",
            conn, tx);
        closeCmd.Parameters.AddWithValue("effectiveTo", effectiveTo);
        closeCmd.Parameters.AddWithValue("currentProfileId", currentProfileId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts a new profile row with the supplied <paramref name="newProfileId"/> and
    /// <paramref name="version"/> as the currently-active profile (effective_to NULL).
    /// Assumes the active-slot lock has already been acquired and (where needed) the
    /// predecessor closed via <see cref="CloseProfileAsync"/>.
    ///
    /// First-insert callers pass <c>version: 1</c> (matches the schema default; explicit
    /// binding keeps the inserted row aligned with the in-memory model).
    /// </summary>
    private static async Task InsertProfileAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        LocalAgreementProfile newProfile, Guid newProfileId, long version, CancellationToken ct)
    {
        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, flex_carryover_max,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by, created_at, version)
            VALUES (
                @profileId, @orgId, @agreementCode, @okVersion,
                @effectiveFrom, NULL,
                @weeklyNormHours, @maxFlexBalance, @flexCarryoverMax,
                @maxOvertimeHoursPerPeriod, @overtimeRequiresPreApproval,
                @createdBy, @createdAt, @version)
            """, conn, tx);
        insertCmd.Parameters.AddWithValue("profileId", newProfileId);
        insertCmd.Parameters.AddWithValue("orgId", newProfile.OrgId);
        insertCmd.Parameters.AddWithValue("agreementCode", newProfile.AgreementCode);
        insertCmd.Parameters.AddWithValue("okVersion", newProfile.OkVersion);
        insertCmd.Parameters.AddWithValue("effectiveFrom", newProfile.EffectiveFrom);
        insertCmd.Parameters.AddWithValue("weeklyNormHours", (object?)newProfile.WeeklyNormHours ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("maxFlexBalance", (object?)newProfile.MaxFlexBalance ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("flexCarryoverMax", (object?)newProfile.FlexCarryoverMax ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("maxOvertimeHoursPerPeriod", (object?)newProfile.MaxOvertimeHoursPerPeriod ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("overtimeRequiresPreApproval", (object?)newProfile.OvertimeRequiresPreApproval ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("createdBy", newProfile.CreatedBy);
        // CreatedAt: prefer the model's value when supplied (caller may have stamped a
        // deterministic timestamp); otherwise stamp now. The DB default would also fire,
        // but explicit binding keeps the inserted row aligned with the in-memory model.
        var createdAt = newProfile.CreatedAt == default ? DateTime.UtcNow : newProfile.CreatedAt;
        insertCmd.Parameters.AddWithValue("createdAt", createdAt);
        insertCmd.Parameters.AddWithValue("version", version);
        await insertCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// UPDATE-in-place path for same-day saves (ADR-018 D9 MODIFIED branch). Updates the
    /// 5 overridable columns and bumps <c>version</c> by one; <c>effective_from</c>,
    /// <c>effective_to</c>, <c>created_by</c>, <c>created_at</c> are immutable across
    /// in-place edits. Returns the post-bump version (sourced from
    /// <c>UPDATE ... RETURNING version</c>).
    ///
    /// The <c>WHERE version = @expectedVersion</c> clause is defense-in-depth (ADR-018 D7):
    /// the load-bearing optimistic-concurrency check happens earlier in
    /// <see cref="ValidatePrecondition"/> and the FOR UPDATE held by
    /// <see cref="AcquireLockAsync"/> under <see cref="IsolationLevel.RepeatableRead"/>
    /// prevents concurrent mutation. The clause guards against a future code path that
    /// bypasses the lock (e.g., a hypothetical bulk-update endpoint) and makes the SQL
    /// self-documenting about the optimistic invariant.
    /// </summary>
    private static async Task<long> UpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid profileId, LocalAgreementProfile newProfile, long expectedVersion,
        CancellationToken ct)
    {
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE local_agreement_profiles
            SET weekly_norm_hours = @weeklyNormHours,
                max_flex_balance = @maxFlexBalance,
                flex_carryover_max = @flexCarryoverMax,
                max_overtime_hours_per_period = @maxOvertimeHoursPerPeriod,
                overtime_requires_pre_approval = @overtimeRequiresPreApproval,
                version = version + 1
            WHERE profile_id = @profileId
              AND version = @expectedVersion
            RETURNING version
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("weeklyNormHours", (object?)newProfile.WeeklyNormHours ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("maxFlexBalance", (object?)newProfile.MaxFlexBalance ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("flexCarryoverMax", (object?)newProfile.FlexCarryoverMax ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("maxOvertimeHoursPerPeriod", (object?)newProfile.MaxOvertimeHoursPerPeriod ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("overtimeRequiresPreApproval", (object?)newProfile.OvertimeRequiresPreApproval ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("profileId", profileId);
        updateCmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        var newVersion = await updateCmd.ExecuteScalarAsync(ct);
        if (newVersion is null)
        {
            // Defense-in-depth: should be unreachable because the lock under RepeatableRead
            // prevents concurrent mutation between AcquireLockAsync and UpdateInPlaceAsync.
            // If it ever fires, surfacing it loudly is preferable to silently dropping the
            // write or returning a stale version.
            throw new InvalidOperationException(
                $"UpdateInPlaceAsync produced no row for profile_id={profileId} at " +
                $"expected version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        return (long)newVersion;
    }

    /// <summary>
    /// Closes the currently-active profile (if any) by setting <c>effective_to = today</c>
    /// without inserting a successor. Returns the number of rows affected (0 = no current
    /// open profile, 1 = closed). Implements the deactivation-without-supersession case
    /// per ADR-017 D2.
    ///
    /// End-exclusive convention (ADR-018 D8): stamping <c>effective_to = today</c> means
    /// "no longer active starting today" — the deactivation semantic. No <c>+1 day</c>
    /// shift is needed because end-exclusive's lower-inclusive / upper-exclusive interval
    /// expresses the "last active day was yesterday" meaning naturally. The S22 migration
    /// shifted already-closed history rows by <c>+1 day</c> to relabel pre-S22 end-inclusive
    /// values into the new convention; active-row deactivation here stamps the natural
    /// end-exclusive value.
    /// </summary>
    public async Task<int> DeactivateAsync(
        string orgId, string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        // "Today" is computed in UTC — see SupersedeAndCreateAsync for the
        // Phase-4 hardening note on Europe/Copenhagen vs UTC midnight boundaries.
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE local_agreement_profiles
            SET effective_to = @today
            WHERE org_id = @orgId
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("today", DateOnly.FromDateTime(DateTime.UtcNow.Date));
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static LocalAgreementProfile MapReader(NpgsqlDataReader reader) => new()
    {
        ProfileId = reader.GetGuid(reader.GetOrdinal("profile_id")),
        OrgId = reader.GetString(reader.GetOrdinal("org_id")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        EffectiveFrom = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_from"))),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
            ? null
            : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("effective_to"))),
        WeeklyNormHours = reader.IsDBNull(reader.GetOrdinal("weekly_norm_hours"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("weekly_norm_hours")),
        MaxFlexBalance = reader.IsDBNull(reader.GetOrdinal("max_flex_balance"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("max_flex_balance")),
        FlexCarryoverMax = reader.IsDBNull(reader.GetOrdinal("flex_carryover_max"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("flex_carryover_max")),
        MaxOvertimeHoursPerPeriod = reader.IsDBNull(reader.GetOrdinal("max_overtime_hours_per_period"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("max_overtime_hours_per_period")),
        OvertimeRequiresPreApproval = reader.IsDBNull(reader.GetOrdinal("overtime_requires_pre_approval"))
            ? null
            : reader.GetBoolean(reader.GetOrdinal("overtime_requires_pre_approval")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        Version = reader.GetInt64(reader.GetOrdinal("version"))
    };
}

/// <summary>
/// Result of <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync(System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// (and its in-transaction sibling). Carries the post-write profile identity, the version
/// the row holds AFTER the operation, and a flag indicating whether the same-day write
/// was a no-op (S23 / TASK-2304).
///
/// <para>
/// <b>No-op semantics</b>: when an admin re-saves a profile on the same day with no
/// overridable-field change, the repo skips the UPDATE entirely (version unchanged,
/// audit + outbox suppressed by the caller). This branch runs AFTER lock acquisition +
/// optimistic-concurrency precondition validation, so a stale caller cannot hide a
/// version mismatch behind an apparent "no-op" — their <c>If-Match</c> is rejected
/// before this short-circuit is reached. Per ADR-018 D7 + S23 Step 0b plan-mode review
/// (Codex BLOCKER on the original endpoint-level fast path).
/// </para>
///
/// <para>
/// <b>Backward-compat</b>: the 2-arg <c>Deconstruct</c> overload preserves the
/// <c>var (id, version) = ...</c> destructuring shape used across S22-era tests and
/// callers — they continue to compile unchanged. New consumers (the PUT endpoint as of
/// S23) destructure 3-arg and branch on <see cref="IsNoOp"/>.
/// </para>
/// </summary>
public sealed record SaveProfileResult(Guid ProfileId, long Version, bool IsNoOp)
{
    /// <summary>
    /// Backward-compat overload — preserves the S22 destructuring shape
    /// <c>var (id, version) = await repo.SupersedeAndCreateAsync(...)</c>. New code
    /// that needs the no-op signal destructures all three positional fields or
    /// reads <see cref="IsNoOp"/> via property access.
    /// </summary>
    public void Deconstruct(out Guid profileId, out long version)
    {
        profileId = ProfileId;
        version = Version;
    }
}

/// <summary>
/// Thrown by <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync(System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// (and its in-transaction sibling) when the caller's optimistic-concurrency precondition
/// (HTTP <c>If-Match: "&lt;version&gt;"</c> / <c>If-None-Match: *</c>) does not match the
/// version of the row currently holding the active slot. Per ADR-018 D7, the PUT endpoint
/// (TASK-2205) maps this to <c>412 Precondition Failed</c> and returns the current state in
/// the response body.
/// </summary>
public sealed class OptimisticConcurrencyException : Exception
{
    /// <summary>The version the caller asserted was current (null = "no current expected").</summary>
    public long? ExpectedVersion { get; }

    /// <summary>The version actually current at lock time (null = "no current open profile",
    /// or — in the concurrent-first-create branch — the loser couldn't read the winner's row
    /// before the unique-violation fired).</summary>
    public long? ActualVersion { get; }

    public OptimisticConcurrencyException(string message)
        : base(message) { }

    public OptimisticConcurrencyException(string message, long? expectedVersion, long? actualVersion)
        : base(message)
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public OptimisticConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }

    public OptimisticConcurrencyException(
        string message,
        long? expectedVersion,
        long? actualVersion,
        Exception innerException)
        : base(message, innerException)
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}

/// <summary>
/// Thrown by <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync(System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// (and its in-transaction sibling) when a save attempt's <c>effective_from</c> is earlier
/// than the predecessor profile's <c>effective_from</c>. Per ADR-018 D9, backdate-before-
/// predecessor remains rejected (now strict-less under end-exclusive — the off-by-one that
/// motivated S21 cycle 9's removal of this exception is eliminated by ADR-018 D8). The
/// PUT handler (TASK-2205) maps this to <c>400 Bad Request</c>.
/// </summary>
public sealed class InvalidProfileSupersessionException : Exception
{
    public InvalidProfileSupersessionException(string message)
        : base(message) { }

    public InvalidProfileSupersessionException(string message, Exception innerException)
        : base(message, innerException) { }
}
