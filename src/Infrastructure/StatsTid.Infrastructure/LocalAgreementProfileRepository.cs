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
/// WHERE effective_to IS NULL. Save runs inside a single transaction with <c>SELECT ... FOR UPDATE</c>
/// on the current open row to gate concurrent writers. Its one production caller, the PUT route,
/// opens that transaction at <see cref="IsolationLevel.ReadCommitted"/> and takes the pay-inputs lock
/// (<see cref="PayInputsLock"/>) first (S148 PIL; PAT-015); the self-contained overload opens its own
/// at <see cref="IsolationLevel.RepeatableRead"/>.
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
/// numeric version into <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Nullable{ExpectedPredecessor}, System.Threading.CancellationToken)"/>'s
/// <c>expectedCurrentVersion</c> parameter.
///
/// Pattern follows <see cref="LocalConfigurationRepository"/>: read methods open their own
/// connection via the injected <see cref="DbConnectionFactory"/>. Writes have two flavors:
/// <list type="bullet">
/// <item><description>Self-contained:
/// <see cref="SupersedeAndCreateAsync(System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// owns its own connection and transaction. Returns <c>(profileId, version)</c>.</description></item>
/// <item><description>In-transaction sibling:
/// <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Nullable{ExpectedPredecessor}, System.Threading.CancellationToken)"/>
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
    /// The profile in force for the (org, agreement_code, ok_version) triple on <paramref name="day"/>
    /// (ADR-017 D3, ADR-043 D12): the row with <c>effective_from &lt;= day</c> and (<c>effective_to</c> null or
    /// <c>&gt; day</c>), end-exclusive (ADR-018 D8). Rows of one triple never overlap, so at most one covers a
    /// day; <c>null</c> when none does (no local change). The configuration lookup's local layer
    /// (<see cref="ConfigResolutionService"/>), beside the two other in-force reads
    /// (<see cref="AgreementConfigRepository.GetInForceAsync"/>, <see cref="PositionOverrideRepository.GetInForceAsync"/>).
    /// </summary>
    public async Task<LocalAgreementProfile?> GetInForceAsync(
        string orgId, string agreementCode, string okVersion, DateOnly day, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM local_agreement_profiles
            WHERE org_id = @orgId
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_from <= @day
              AND (effective_to IS NULL OR effective_to > @day)
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("day", day);
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
    /// <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Nullable{ExpectedPredecessor}, System.Threading.CancellationToken)"/>.
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
    /// Transaction: this overload opens its own at <see cref="IsolationLevel.RepeatableRead"/> and
    /// takes no pay-inputs lock. It has no production caller (the PUT route calls the in-transaction
    /// overload inside its own READ COMMITTED transaction, after the pay-inputs lock); its callers are
    /// tests. The <c>SELECT ... FOR UPDATE</c> on the open row, with the partial unique index for the
    /// empty slot, serializes concurrent writers to the same (org, agreement_code, ok_version) triple.
    /// </summary>
    /// <returns>The post-write profile id and version (and whether the same-day write was
    /// a no-op — <see cref="SaveProfileResult.IsNoOp"/>). For first-create + supersession the
    /// version is <c>1</c>; for UPDATE-in-place it is <c>predecessor.Version + 1</c> unless
    /// the candidate's settable fields all match the predecessor (S23 / TASK-2304 same-day
    /// no-op short-circuit; <see cref="AllOverridableFieldsMatch"/>), in which case the
    /// predecessor's version is returned unchanged and the caller skips audit + outbox emission.</returns>
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
            var result = await SupersedeAndCreateAsync(conn, tx, expectedCurrentVersion, newProfile, ct: ct);
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
    /// Concurrency semantics and exception contract are identical to the self-contained overload.
    /// Isolation: the row lock and the partial unique index give the same guarantees at
    /// <see cref="IsolationLevel.ReadCommitted"/>, where the PUT route runs it (S148 PIL): its
    /// transaction's first statement waits for the pay-inputs lock, and under REPEATABLE READ the
    /// snapshot would predate what committed during that wait (PAT-015). The result names the row
    /// this save locked (<see cref="SaveProfileResult.LockedPredecessorProfileId"/>,
    /// <see cref="SaveProfileResult.LockedPredecessorVersion"/>).
    /// </summary>
    /// <param name="expectedPredecessor">The open row the caller read before its transaction and
    /// computed its delta against (S148 ruling A9; TASK-14823 Step 5a ruling R1):
    /// its profile id, or a <c>null</c> id when it read no open row. When supplied, the locked open
    /// row must be that row, checked under the lock, after the version check and before any
    /// date-dependent validation; otherwise <see cref="OptimisticConcurrencyException"/>. A
    /// supersession restarts the version at 1, so the version check alone can pass against a
    /// different row. <c>null</c> (not supplied) skips the check.</param>
    /// <returns>The post-write profile id, version, and a no-op flag. See <see cref="SaveProfileResult"/>
    /// for semantics. For first-create + supersession the version is <c>1</c>; for UPDATE-in-place
    /// it is <c>predecessor.Version + 1</c>; for the S23 same-day no-op path it is
    /// <c>predecessor.Version</c> (unchanged) with <see cref="SaveProfileResult.IsNoOp"/> = true.</returns>
    /// <exception cref="OptimisticConcurrencyException">If the precondition encoded in
    /// <paramref name="expectedCurrentVersion"/> does not match the row currently holding the
    /// active slot, or that row is not <paramref name="expectedPredecessor"/>.</exception>
    /// <exception cref="InvalidProfileSupersessionException">If
    /// <c>newProfile.EffectiveFrom &lt; predecessor.EffectiveFrom</c>.</exception>
    public async Task<SaveProfileResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long? expectedCurrentVersion,
        LocalAgreementProfile newProfile,
        ExpectedPredecessor? expectedPredecessor = null,
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

        // 2b. The stale predecessor (S148 ruling A9; TASK-14823 Step 5a ruling R1). The locked
        //     open row must be the one the caller read before its transaction: the same profile
        //     id, or both absent. The version check above can pass against a row that replaced
        //     it during the wait, because a supersession restarts the version at 1. Checked here,
        //     before the backdate guard (3a) and the same-day branch (3b), so a replaced
        //     predecessor always answers "refresh and retry" (412 at the PUT route), never a date
        //     error about a row the caller never saw. Not supplied (null): no check.
        if (expectedPredecessor is { } expected && current?.ProfileId != expected.ProfileId)
        {
            throw new OptimisticConcurrencyException(
                "The current profile changed after this request read it; refresh and retry.",
                expectedVersion: expectedCurrentVersion,
                actualVersion: current?.Version);
        }

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
                // 3b-noop. S23 / TASK-2304 — short-circuit when no settable field changed.
                //     Detection happens AFTER lock + ValidatePrecondition (Codex Step 0b
                //     BLOCKER fix): a stale caller cannot reach this path because their
                //     If-Match is rejected by ValidatePrecondition first. If we reach here
                //     and every settable field matches the predecessor, do NOT bump
                //     version, do NOT UPDATE the row. Return predecessor.Version unchanged
                //     with IsNoOp = true; the PUT handler skips audit + outbox emission.
                if (AllOverridableFieldsMatch(predecessor, newProfile))
                {
                    return new SaveProfileResult(predecessor.ProfileId, predecessor.Version, IsNoOp: true)
                    {
                        LockedPredecessorProfileId = predecessor.ProfileId,
                        LockedPredecessorVersion = predecessor.Version,
                    };
                }

                // 3b-update. UPDATE-in-place. profile_id stable; version bumped by one;
                //     effective_from / effective_to immutable. The audit-action at the call
                //     site is MODIFIED (vs SUPERSEDED for 3c below).
                var newVersion = await UpdateInPlaceAsync(
                    conn, tx, predecessor.ProfileId, newProfile, predecessor.Version, ct);
                return new SaveProfileResult(predecessor.ProfileId, newVersion, IsNoOp: false)
                {
                    LockedPredecessorProfileId = predecessor.ProfileId,
                    LockedPredecessorVersion = predecessor.Version,
                };
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
        return new SaveProfileResult(newProfileId, Version: 1, IsNoOp: false)
        {
            LockedPredecessorProfileId = current?.ProfileId,
            LockedPredecessorVersion = current?.Version,
        };
    }

    /// <summary>
    /// Returns true when every settable field on <paramref name="predecessor"/> equals the
    /// corresponding value on <paramref name="candidate"/>. Used by the S23 same-day no-op
    /// short-circuit to skip the UPDATE + audit + outbox emission when an admin saves a profile
    /// with no field changes (typical "open the form, change nothing, hit Save" admin flow).
    /// A save that changes only one setting is never a no-op.
    ///
    /// <para>
    /// The settable fields are every column the save writes (<see cref="InsertProfileAsync"/>,
    /// <see cref="UpdateInPlaceAsync"/>; the schema columns ARE the whitelist per ADR-017 D9a,
    /// protected keys are absent from the schema by design), in the same order as the PUT
    /// route's <c>ComputeChangedFields</c>, so the no-op decision and the audit delta cannot
    /// disagree: the four legacy overridable fields <c>WeeklyNormHours</c>,
    /// <c>MaxFlexBalance</c>, <c>MaxOvertimeHoursPerPeriod</c>,
    /// <c>OvertimeRequiresPreApproval</c>; the flex settings <c>FlexSettlementPeriod</c>,
    /// <c>FlexCapTestPeriod</c>, <c>FlexPlusCapHours</c>, <c>FlexMinusCapHours</c>,
    /// <c>FlexDailyCreditCapHours</c>, <c>FlexAboveCapAction</c>, <c>FlexLeavingAction</c>;
    /// the ten office-hours times <c>OfficeHours{Mon..Fri}{Start,End}</c>; and
    /// <c>PaidAbsenceCreditBasis</c> (ADR-043 D6). C# nullable equality (<c>==</c>) handles the
    /// (null, null) ⇒ true case for <c>decimal?</c> (by value: 37 equals 37.00),
    /// <c>bool?</c> and <c>TimeOnly?</c>; strings compare ordinally. Identity
    /// (<c>profile_id</c>, <c>effective_from</c>, <c>created_by</c>, <c>created_at</c>,
    /// <c>version</c>) and lifecycle (<c>effective_to</c>) columns are immutable across in-place
    /// edits and therefore irrelevant to this check.
    /// </para>
    /// </summary>
    private static bool AllOverridableFieldsMatch(
        LocalAgreementProfile predecessor, LocalAgreementProfile candidate)
    {
        return predecessor.WeeklyNormHours == candidate.WeeklyNormHours
            && predecessor.MaxFlexBalance == candidate.MaxFlexBalance
            && predecessor.MaxOvertimeHoursPerPeriod == candidate.MaxOvertimeHoursPerPeriod
            && predecessor.OvertimeRequiresPreApproval == candidate.OvertimeRequiresPreApproval
            && string.Equals(predecessor.FlexSettlementPeriod, candidate.FlexSettlementPeriod, StringComparison.Ordinal)
            && string.Equals(predecessor.FlexCapTestPeriod, candidate.FlexCapTestPeriod, StringComparison.Ordinal)
            && predecessor.FlexPlusCapHours == candidate.FlexPlusCapHours
            && predecessor.FlexMinusCapHours == candidate.FlexMinusCapHours
            && predecessor.FlexDailyCreditCapHours == candidate.FlexDailyCreditCapHours
            && string.Equals(predecessor.FlexAboveCapAction, candidate.FlexAboveCapAction, StringComparison.Ordinal)
            && string.Equals(predecessor.FlexLeavingAction, candidate.FlexLeavingAction, StringComparison.Ordinal)
            && predecessor.OfficeHoursMonStart == candidate.OfficeHoursMonStart
            && predecessor.OfficeHoursMonEnd == candidate.OfficeHoursMonEnd
            && predecessor.OfficeHoursTueStart == candidate.OfficeHoursTueStart
            && predecessor.OfficeHoursTueEnd == candidate.OfficeHoursTueEnd
            && predecessor.OfficeHoursWedStart == candidate.OfficeHoursWedStart
            && predecessor.OfficeHoursWedEnd == candidate.OfficeHoursWedEnd
            && predecessor.OfficeHoursThuStart == candidate.OfficeHoursThuStart
            && predecessor.OfficeHoursThuEnd == candidate.OfficeHoursThuEnd
            && predecessor.OfficeHoursFriStart == candidate.OfficeHoursFriStart
            && predecessor.OfficeHoursFriEnd == candidate.OfficeHoursFriEnd
            && string.Equals(predecessor.PaidAbsenceCreditBasis, candidate.PaidAbsenceCreditBasis, StringComparison.Ordinal);
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
    /// <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Nullable{ExpectedPredecessor}, System.Threading.CancellationToken)"/>
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
                weekly_norm_hours, max_flex_balance,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                flex_settlement_period, flex_cap_test_period,
                flex_plus_cap_hours, flex_minus_cap_hours, flex_daily_credit_cap_hours,
                flex_above_cap_action, flex_leaving_action,
                office_hours_mon_start, office_hours_mon_end,
                office_hours_tue_start, office_hours_tue_end,
                office_hours_wed_start, office_hours_wed_end,
                office_hours_thu_start, office_hours_thu_end,
                office_hours_fri_start, office_hours_fri_end,
                paid_absence_credit_basis,
                created_by, created_at, version)
            VALUES (
                @profileId, @orgId, @agreementCode, @okVersion,
                @effectiveFrom, NULL,
                @weeklyNormHours, @maxFlexBalance,
                @maxOvertimeHoursPerPeriod, @overtimeRequiresPreApproval,
                @flexSettlementPeriod, @flexCapTestPeriod,
                @flexPlusCapHours, @flexMinusCapHours, @flexDailyCreditCapHours,
                @flexAboveCapAction, @flexLeavingAction,
                @officeHoursMonStart, @officeHoursMonEnd,
                @officeHoursTueStart, @officeHoursTueEnd,
                @officeHoursWedStart, @officeHoursWedEnd,
                @officeHoursThuStart, @officeHoursThuEnd,
                @officeHoursFriStart, @officeHoursFriEnd,
                @paidAbsenceCreditBasis,
                @createdBy, @createdAt, @version)
            """, conn, tx);
        insertCmd.Parameters.AddWithValue("profileId", newProfileId);
        insertCmd.Parameters.AddWithValue("orgId", newProfile.OrgId);
        insertCmd.Parameters.AddWithValue("agreementCode", newProfile.AgreementCode);
        insertCmd.Parameters.AddWithValue("okVersion", newProfile.OkVersion);
        insertCmd.Parameters.AddWithValue("effectiveFrom", newProfile.EffectiveFrom);
        AddSettableColumnParameters(insertCmd, newProfile);
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
    /// UPDATE-in-place path for same-day saves (ADR-018 D9 MODIFIED branch). Updates every
    /// settable column (the ones <see cref="AllOverridableFieldsMatch"/> compares) and bumps
    /// <c>version</c> by one; <c>effective_from</c>, <c>effective_to</c>, <c>created_by</c>,
    /// <c>created_at</c> are immutable across in-place edits. Returns the post-bump version
    /// (sourced from <c>UPDATE ... RETURNING version</c>).
    ///
    /// The <c>WHERE version = @expectedVersion</c> clause is defense-in-depth (ADR-018 D7):
    /// the load-bearing optimistic-concurrency check happens earlier in
    /// <see cref="ValidatePrecondition"/> and the FOR UPDATE held by
    /// <see cref="AcquireLockAsync"/> prevents concurrent mutation. The clause guards against a future code path that
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
                max_overtime_hours_per_period = @maxOvertimeHoursPerPeriod,
                overtime_requires_pre_approval = @overtimeRequiresPreApproval,
                flex_settlement_period = @flexSettlementPeriod,
                flex_cap_test_period = @flexCapTestPeriod,
                flex_plus_cap_hours = @flexPlusCapHours,
                flex_minus_cap_hours = @flexMinusCapHours,
                flex_daily_credit_cap_hours = @flexDailyCreditCapHours,
                flex_above_cap_action = @flexAboveCapAction,
                flex_leaving_action = @flexLeavingAction,
                office_hours_mon_start = @officeHoursMonStart,
                office_hours_mon_end = @officeHoursMonEnd,
                office_hours_tue_start = @officeHoursTueStart,
                office_hours_tue_end = @officeHoursTueEnd,
                office_hours_wed_start = @officeHoursWedStart,
                office_hours_wed_end = @officeHoursWedEnd,
                office_hours_thu_start = @officeHoursThuStart,
                office_hours_thu_end = @officeHoursThuEnd,
                office_hours_fri_start = @officeHoursFriStart,
                office_hours_fri_end = @officeHoursFriEnd,
                paid_absence_credit_basis = @paidAbsenceCreditBasis,
                version = version + 1
            WHERE profile_id = @profileId
              AND version = @expectedVersion
            RETURNING version
            """, conn, tx);
        AddSettableColumnParameters(updateCmd, newProfile);
        updateCmd.Parameters.AddWithValue("profileId", profileId);
        updateCmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        var newVersion = await updateCmd.ExecuteScalarAsync(ct);
        if (newVersion is null)
        {
            // Defense-in-depth: should be unreachable because the row lock
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
    /// Binds every settable column's parameter, NULL as <see cref="DBNull"/>, for both
    /// <see cref="InsertProfileAsync"/> and <see cref="UpdateInPlaceAsync"/>, so the two writes
    /// cannot store different column sets. The members and order are
    /// <see cref="AllOverridableFieldsMatch"/>'s and <see cref="MapReader"/>'s.
    /// </summary>
    private static void AddSettableColumnParameters(NpgsqlCommand cmd, LocalAgreementProfile p)
    {
        cmd.Parameters.AddWithValue("weeklyNormHours", (object?)p.WeeklyNormHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("maxFlexBalance", (object?)p.MaxFlexBalance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("maxOvertimeHoursPerPeriod", (object?)p.MaxOvertimeHoursPerPeriod ?? DBNull.Value);
        cmd.Parameters.AddWithValue("overtimeRequiresPreApproval", (object?)p.OvertimeRequiresPreApproval ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexSettlementPeriod", (object?)p.FlexSettlementPeriod ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexCapTestPeriod", (object?)p.FlexCapTestPeriod ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexPlusCapHours", (object?)p.FlexPlusCapHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexMinusCapHours", (object?)p.FlexMinusCapHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexDailyCreditCapHours", (object?)p.FlexDailyCreditCapHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexAboveCapAction", (object?)p.FlexAboveCapAction ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexLeavingAction", (object?)p.FlexLeavingAction ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursMonStart", (object?)p.OfficeHoursMonStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursMonEnd", (object?)p.OfficeHoursMonEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursTueStart", (object?)p.OfficeHoursTueStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursTueEnd", (object?)p.OfficeHoursTueEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursWedStart", (object?)p.OfficeHoursWedStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursWedEnd", (object?)p.OfficeHoursWedEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursThuStart", (object?)p.OfficeHoursThuStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursThuEnd", (object?)p.OfficeHoursThuEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursFriStart", (object?)p.OfficeHoursFriStart ?? DBNull.Value);
        cmd.Parameters.AddWithValue("officeHoursFriEnd", (object?)p.OfficeHoursFriEnd ?? DBNull.Value);
        cmd.Parameters.AddWithValue("paidAbsenceCreditBasis", (object?)p.PaidAbsenceCreditBasis ?? DBNull.Value);
    }

    // S142 / TASK-14208 (owner ruling OQ-4): DeactivateAsync was DELETED here, not migrated.
    // It closed the live profile at `DateOnly.FromDateTime(DateTime.UtcNow.Date)` — the UTC
    // calendar day used as a BUSINESS date — and its own comment conceded the gap
    // ("'Today' is computed in UTC — see SupersedeAndCreateAsync for the Phase-4 hardening note
    // on Europe/Copenhagen vs UTC midnight boundaries"). It also read the wall clock directly
    // rather than an injected TimeProvider, so it was not testable at a pinned instant at all.
    // Nothing called it: the only `DeactivateAsync` call sites in the repository resolve to
    // PositionOverrideRepository (PositionOverrideEndpoints) and ProjectRepository
    // (ProjectEndpoints) — never this one. It described the ADR-017 D2
    // deactivation-without-supersession case; if that case is ever needed, the close-stamp must
    // be an application-supplied Copenhagen business date (SharedKernel CopenhagenBusinessDate)
    // bound as a parameter, and the end-exclusive [from, to) reasoning the old doc comment gave
    // still holds: stamping effective_to = today means "no longer active starting today", with
    // no +1-day shift, because the interval is upper-exclusive (ADR-018 D9).

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
        MaxOvertimeHoursPerPeriod = reader.IsDBNull(reader.GetOrdinal("max_overtime_hours_per_period"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("max_overtime_hours_per_period")),
        OvertimeRequiresPreApproval = reader.IsDBNull(reader.GetOrdinal("overtime_requires_pre_approval"))
            ? null
            : reader.GetBoolean(reader.GetOrdinal("overtime_requires_pre_approval")),
        FlexSettlementPeriod = GetNullableString(reader, "flex_settlement_period"),
        FlexCapTestPeriod = GetNullableString(reader, "flex_cap_test_period"),
        FlexPlusCapHours = GetNullableDecimal(reader, "flex_plus_cap_hours"),
        FlexMinusCapHours = GetNullableDecimal(reader, "flex_minus_cap_hours"),
        FlexDailyCreditCapHours = GetNullableDecimal(reader, "flex_daily_credit_cap_hours"),
        FlexAboveCapAction = GetNullableString(reader, "flex_above_cap_action"),
        FlexLeavingAction = GetNullableString(reader, "flex_leaving_action"),
        OfficeHoursMonStart = GetNullableTime(reader, "office_hours_mon_start"),
        OfficeHoursMonEnd = GetNullableTime(reader, "office_hours_mon_end"),
        OfficeHoursTueStart = GetNullableTime(reader, "office_hours_tue_start"),
        OfficeHoursTueEnd = GetNullableTime(reader, "office_hours_tue_end"),
        OfficeHoursWedStart = GetNullableTime(reader, "office_hours_wed_start"),
        OfficeHoursWedEnd = GetNullableTime(reader, "office_hours_wed_end"),
        OfficeHoursThuStart = GetNullableTime(reader, "office_hours_thu_start"),
        OfficeHoursThuEnd = GetNullableTime(reader, "office_hours_thu_end"),
        OfficeHoursFriStart = GetNullableTime(reader, "office_hours_fri_start"),
        OfficeHoursFriEnd = GetNullableTime(reader, "office_hours_fri_end"),
        PaidAbsenceCreditBasis = GetNullableString(reader, "paid_absence_credit_basis"),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        Version = reader.GetInt64(reader.GetOrdinal("version"))
    };

    private static string? GetNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static decimal? GetNullableDecimal(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
    }

    /// <summary>A PostgreSQL <c>time</c> column as <see cref="TimeOnly"/> (Npgsql's mapping).</summary>
    private static TimeOnly? GetNullableTime(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<TimeOnly>(ordinal);
    }
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
    /// The profile id of the open row the save locked (<c>SELECT ... FOR UPDATE</c>) as the one it
    /// replaces or edits, or <c>null</c> when no open row existed. The decisive identity check is
    /// the repository's own, under the lock and before the date checks, when the caller passes
    /// <see cref="ExpectedPredecessor"/> (S148 ruling A9; TASK-14823 Step 5a ruling R1). The PUT
    /// route also compares this member and <see cref="LockedPredecessorVersion"/> with the row it
    /// read before its transaction, as a second guard that adds the version.
    /// </summary>
    public Guid? LockedPredecessorProfileId { get; init; }

    /// <summary>The version of the locked row named by <see cref="LockedPredecessorProfileId"/>, as
    /// locked (before this save's write); <c>null</c> when no open row existed.</summary>
    public long? LockedPredecessorVersion { get; init; }

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
/// The open row a caller of the in-transaction
/// <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{long}, LocalAgreementProfile, System.Nullable{ExpectedPredecessor}, System.Threading.CancellationToken)"/>
/// read before its transaction: <see cref="ProfileId"/> is that row's profile id, or <c>null</c> when
/// the caller read no open row. A supplied value (even with a <c>null</c> id) asks the repository to
/// refuse with <see cref="OptimisticConcurrencyException"/> when the locked open row is a different
/// row; an absent value (<c>null</c> of this type) asks for no check (S148 ruling A9; TASK-14823
/// Step 5a ruling R1).
/// </summary>
public readonly record struct ExpectedPredecessor(Guid? ProfileId);

/// <summary>
/// Thrown by <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync(System.Nullable{long}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// (and its in-transaction sibling) when the caller's optimistic-concurrency precondition
/// (HTTP <c>If-Match: "&lt;version&gt;"</c> / <c>If-None-Match: *</c>) does not match the
/// version of the row currently holding the active slot, or (S148 TASK-14823, ruling A9) when the version matches but the
/// locked open row is not the row the caller read before the lock (a different profile id, or one present where the
/// other is absent). Per ADR-018 D7, the PUT endpoint
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
