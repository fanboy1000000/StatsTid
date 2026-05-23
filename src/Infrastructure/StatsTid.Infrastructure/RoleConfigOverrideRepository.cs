using System.Text.Json;
using Npgsql;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S40 / TASK-4003 — Phase 4e versioned-history store for role-within-agreement
/// overrides (ADR-024 D1 + D2). 5th versioned-config repository after WTM (S29),
/// EntitlementConfig (S30), EmployeeProfile (S31), and UserAgreementCode (S34).
/// Composite natural key <c>(employment_category, agreement_code, ok_version)</c>;
/// bitemporal <c>(effective_from, effective_to)</c> under end-exclusive
/// <c>[from, to)</c> semantics (ADR-018 D9); monotonic <c>version</c> column for
/// ADR-019 admin-strict If-Match optimistic concurrency. The partial-unique-index
/// <c>idx_role_config_overrides_live</c> enforces "at most one live (open) row per
/// natural-key triple".
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D5).</b> All writes are
/// <c>(NpgsqlConnection, NpgsqlTransaction)</c> overloads — the endpoint (S41) owns
/// the transaction, threading audit + outbox writes into the same atomic unit. Reads
/// expose both a self-managed overload (admin GET, ConfigResolutionService cache
/// warmup) and a <c>(conn, tx)</c> sibling for read-then-write atomicity (ADR-018 D3).
/// </para>
///
/// <para>
/// <b>ADR-020 D2 3-case routing</b> in <see cref="SupersedeAndCreateAsync"/>:
/// Case A INSERT v=1, Case B same-day UPDATE-in-place v→v+1, Case C cross-day
/// close-predecessor + INSERT successor at <c>predecessor.Version + 1</c> per S33 / S34
/// ETag-monotonicity refinement (composite single-row key forces version to carry the
/// monotonic load alone — natural key does not include effective_from).
/// </para>
///
/// <para>
/// <b>ADR-023 D8 SoftDelete</b> in <see cref="SoftDeleteAsync"/>: stamps
/// <c>effective_to = NOW()::date</c> with the version column UNCHANGED. After the call
/// the row "disappears" from live reads via the partial-unique-index predicate. The
/// caller-emitted audit row records <c>version_before = version_after = version</c>
/// per ADR-019 D8. A retry with stale If-Match after a successful soft-delete maps to
/// 404 Not Found (row-disappearance idempotency), NOT 412 — mirrors S33
/// EmployeeProfileRepository semantics.
/// </para>
///
/// <para>
/// <b>Audit emission is endpoint-owned</b> (ADR-019 D8). This repository's
/// <see cref="AppendAuditAsync"/> is a v3 atomic-outbox primitive (mirrors S24
/// AgreementConfigRepository's audit-bearing Pattern B trio); the endpoint composes
/// the JSON payloads + version-transition pair and threads the call through the same
/// tx as the write — <see cref="SupersedeAndCreateAsync"/> and
/// <see cref="SoftDeleteAsync"/> do NOT write audit rows themselves.
/// </para>
/// </summary>
public sealed class RoleConfigOverrideRepository
{
    private const string ResourceType = "role_config_overrides";

    private readonly DbConnectionFactory _dbFactory;

    public RoleConfigOverrideRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ------------------------------------------------------------------
    // Reads — self-managed + in-tx overload + dated lookup.
    // ------------------------------------------------------------------

    /// <summary>
    /// S40 / TASK-4003 — convenience read of the live row for the natural-key triple
    /// <paramref name="employmentCategory"/> / <paramref name="agreementCode"/> /
    /// <paramref name="okVersion"/>, or <c>null</c> when no live row exists. Consumed
    /// by admin GET handlers (S41) and by ConfigResolutionService cache warmup paths
    /// that don't need transactional read-then-write atomicity.
    ///
    /// <para>
    /// <b>LIVE-only.</b> Returns the row whose <c>effective_to IS NULL</c>; past-period
    /// replay-sensitive callers must use
    /// <see cref="GetByEmploymentCategoryAtAsync"/> instead (ADR-016 D10 + ADR-018 D14
    /// export-time effective-date lookup).
    /// </para>
    /// </summary>
    public async Task<RoleConfigOverride?> GetCurrentAsync(
        string employmentCategory, string agreementCode, string okVersion,
        CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetCurrentAsync(conn, null, employmentCategory, agreementCode, okVersion, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="GetCurrentAsync(string, string, string, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so
    /// the read sits inside the same transaction as a downstream write (ADR-018 D3
    /// atomic-outbox contract). Used by S41 admin endpoints that need to read the
    /// pre-mutation row for the audit-payload <c>previous_data</c> JSON snapshot.
    /// </summary>
    public async Task<RoleConfigOverride?> GetCurrentAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employmentCategory, string agreementCode, string okVersion,
        CancellationToken ct = default)
    {
        return await ExecuteGetCurrentAsync(conn, tx, employmentCategory, agreementCode, okVersion, ct);
    }

    /// <summary>
    /// S40 / TASK-4003 — dated lookup for the row whose history window
    /// <c>[effective_from, effective_to)</c> contains <paramref name="asOfDate"/> for
    /// the natural-key triple. Returns <c>null</c> if no row covers that date for the
    /// key. Used by replay-sensitive consumers per ADR-016 D10 + ADR-018 D14
    /// export-time effective-date lookup pattern (S41 ConfigResolutionService replay
    /// cutover): payroll export effective-date lookup, PCS planner snapshot
    /// resolution, and any other past-period boundary computation MUST route through
    /// this method — never read the live partial-index for replay-sensitive paths.
    ///
    /// <para>
    /// <b>End-exclusive predicate</b> per ADR-018 D9: a row with
    /// <c>effective_to = '2026-06-01'</c> covers <c>2026-05-31</c> but NOT
    /// <c>2026-06-01</c> — boundary days belong to the successor. Live rows
    /// (<c>effective_to IS NULL</c>) cover every day from <c>effective_from</c>
    /// forward.
    /// </para>
    /// </summary>
    public async Task<RoleConfigOverride?> GetByEmploymentCategoryAtAsync(
        string employmentCategory, string agreementCode, string okVersion, DateOnly asOfDate,
        CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        const string sql =
            """
            SELECT *
            FROM role_config_overrides
            WHERE employment_category = @employmentCategory
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_from <= @asOfDate
              AND (effective_to IS NULL OR effective_to > @asOfDate)
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("employmentCategory", employmentCategory);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    private static async Task<RoleConfigOverride?> ExecuteGetCurrentAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employmentCategory, string agreementCode, string okVersion,
        CancellationToken ct)
    {
        const string sql =
            """
            SELECT *
            FROM role_config_overrides
            WHERE employment_category = @employmentCategory
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            """;
        await using var cmd = tx is null
            ? new NpgsqlCommand(sql, conn)
            : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("employmentCategory", employmentCategory);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    // ------------------------------------------------------------------
    // Writes — atomic-outbox (conn, tx) overloads only (ADR-018 D5).
    // ADR-020 D2 3-case routing under SELECT ... FOR UPDATE.
    // ------------------------------------------------------------------

    /// <summary>
    /// S40 / TASK-4003 — ADR-020 D2 3-case routing under
    /// <c>SELECT ... FOR UPDATE</c>. Canonical write path for role config overrides.
    ///
    /// <para>
    /// <b>Routing</b> (decided after acquiring a row-level lock on the live row, if
    /// any, for the natural-key triple via <c>SELECT ... FOR UPDATE</c>):
    /// <list type="bullet">
    ///   <item><description><b>Case A — Created.</b> No live row exists. Allowed only
    ///     when <paramref name="expectedVersion"/> is <c>null</c> (admin-POST path).
    ///     INSERT a fresh row at
    ///     <c>(effective_from = effectiveFrom, effective_to = NULL, version = 1)</c>.
    ///     Returns <see cref="SaveOutcome.Created"/>.</description></item>
    ///   <item><description><b>Case B — UpdatedInPlace.</b> Live row exists and its
    ///     <c>effective_from</c> equals <paramref name="effectiveFrom"/>. UPDATE
    ///     in-place: refresh fields, bump <c>version = version + 1</c>;
    ///     <c>override_id</c> and <c>effective_from</c> are immutable. Returns
    ///     <see cref="SaveOutcome.UpdatedInPlace"/>.</description></item>
    ///   <item><description><b>Case C — Superseded.</b> Live row exists and its
    ///     <c>effective_from</c> is strictly earlier than
    ///     <paramref name="effectiveFrom"/>. Close the predecessor by stamping
    ///     <c>effective_to = effectiveFrom</c> (end-exclusive per ADR-018 D9;
    ///     <b>version unchanged</b>), then INSERT a new live row at
    ///     <c>(effective_from = effectiveFrom, effective_to = NULL,
    ///     version = predecessor.Version + 1)</c> per S33 / S34 Step 7a P1
    ///     ETag-monotonicity refinement (composite natural key without effective_from
    ///     forces the version column to carry the monotonic load alone). Returns
    ///     <see cref="SaveOutcome.Superseded"/>.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Optimistic concurrency (ADR-019 admin-strict If-Match).</b>
    /// When <paramref name="expectedVersion"/> is <b>non-null</b>:
    /// <list type="bullet">
    ///   <item><description>No live row → throws
    ///     <see cref="OptimisticConcurrencyException"/> with
    ///     <c>ActualVersion = null</c> (caller asserted a current state that does not
    ///     exist; degenerate mismatch).</description></item>
    ///   <item><description>Live row, version differs → throws
    ///     <see cref="OptimisticConcurrencyException"/> with the actual stored
    ///     version.</description></item>
    /// </list>
    /// When <paramref name="expectedVersion"/> is <b>null</b> (admin-POST create), no
    /// version check is performed; Case A is allowed and Cases B/C proceed unguarded.
    /// </para>
    ///
    /// <para>
    /// <b>Backdate guard.</b> When
    /// <paramref name="effectiveFrom"/> &lt; predecessor.EffectiveFrom, throws
    /// <see cref="InvalidProfileSupersessionException"/> (reused name predates S40 —
    /// shared with WTM / EmployeeProfile / UAC).
    /// </para>
    ///
    /// <para>
    /// <b>Concurrent Case A race.</b> Two concurrent admin-POSTs both observing "no
    /// live row" before the partial-unique-index serializes their INSERTs collide on
    /// 23505. The loser's <c>PostgresException(SqlState="23505")</c> is re-thrown as
    /// <see cref="ConcurrentSeedConflictException"/> with <c>ResourceType =
    /// "role_config_overrides"</c> and <c>ResourceKey</c> = composite
    /// <c>"{employment_category}|{agreement_code}|{ok_version}"</c> so the endpoint
    /// can map it to 409 Conflict per S35 precedent.
    /// </para>
    ///
    /// <para>
    /// <b>Atomic-outbox contract (ADR-018 D5).</b> Caller owns the transaction; this
    /// method only writes to <c>role_config_overrides</c>. The endpoint (S41) emits
    /// the audit row (via <see cref="AppendAuditAsync"/>) + outbox event in the same
    /// tx after this returns, sourcing the event type from
    /// <see cref="SaveRoleConfigOverrideResult.Outcome"/>.
    /// </para>
    /// </summary>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when <paramref name="expectedVersion"/> is non-null and (a) no live row
    /// exists or (b) the live row's <c>version</c> column differs from
    /// <paramref name="expectedVersion"/>. Endpoint maps to 412 per ADR-019.
    /// </exception>
    /// <exception cref="InvalidProfileSupersessionException">
    /// Thrown when <paramref name="effectiveFrom"/> is strictly earlier than the
    /// predecessor's <c>effective_from</c> (backdate rejected per ADR-018 D9 strict-
    /// less under end-exclusive). Endpoint maps to 400/422.
    /// </exception>
    /// <exception cref="ConcurrentSeedConflictException">
    /// Thrown when the Case A INSERT loses a concurrent-create race on the
    /// partial-unique-index <c>idx_role_config_overrides_live</c> (SqlState 23505).
    /// Endpoint maps to 409 Conflict per S35 symmetric mapping.
    /// </exception>
    public async Task<SaveRoleConfigOverrideResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employmentCategory, string agreementCode, string okVersion,
        RoleConfigOverrideFields newFields,
        DateOnly effectiveFrom,
        string actorId, string actorRole,
        long? expectedVersion,
        CancellationToken ct = default)
    {
        // 1. SELECT ... FOR UPDATE the live row (if any). The partial-unique-index
        //    idx_role_config_overrides_live guarantees at most one matching row; the
        //    row-level lock serializes concurrent writers attempting to supersede or
        //    update the same natural-key triple. Mirrors S34 UAC + S33 EmployeeProfile
        //    precedent.
        var predecessorNullable = await AcquireLockAsync(
            conn, tx, employmentCategory, agreementCode, okVersion, ct);

        // 2. Case A — no live row.
        if (predecessorNullable is null)
        {
            if (expectedVersion is not null)
            {
                // Caller asserted a current version, but there is no live row →
                // degenerate mismatch (412). ActualVersion = null distinguishes this
                // branch from the "live row exists, version differs" branch below.
                throw new OptimisticConcurrencyException(
                    $"No live role config override exists for " +
                    $"(employment_category='{employmentCategory}', agreement_code='{agreementCode}', " +
                    $"ok_version='{okVersion}'), but caller sent If-Match: \"{expectedVersion.Value}\"; " +
                    $"refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            // Case A: no predecessor → version=1 baseline. S35 / TASK-3502 precedent —
            // catch PostgresException SqlState 23505 from the partial-unique-index
            // idx_role_config_overrides_live and re-throw as the typed
            // ConcurrentSeedConflictException so the endpoint can map it to 409
            // Conflict. Only Case A can hit this — Case B updates the locked row
            // in-place (no INSERT) and Case C inserts at a new effective_from AFTER
            // closing the predecessor (effective_to non-NULL on predecessor, so the
            // partial-unique-index admits the new row by construction).
            try
            {
                var (newOverrideId, newVersion) = await InsertLiveRowAsync(
                    conn, tx,
                    employmentCategory, agreementCode, okVersion, newFields,
                    effectiveFrom, actorId, actorRole,
                    nextVersion: 1L, ct);
                return new SaveRoleConfigOverrideResult(newOverrideId, newVersion, SaveOutcome.Created);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                throw new ConcurrentSeedConflictException(
                    ResourceType,
                    $"{employmentCategory}|{agreementCode}|{okVersion}");
            }
        }

        // Hoist out of the nullable tuple now that we've eliminated the null branch.
        var predecessor = predecessorNullable.Value;

        // 3. Predecessor exists. Validate optimistic concurrency (when If-Match supplied).
        if (expectedVersion is not null && predecessor.Version != expectedVersion.Value)
        {
            throw new OptimisticConcurrencyException(
                $"Role config override version is {predecessor.Version}, but caller sent " +
                $"If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: predecessor.Version);
        }

        // 4. Backdate guard (ADR-018 D9 strict-less under end-exclusive). A new row
        //    cannot start before its predecessor — there is no valid history window
        //    for the predecessor in that case. Mirrors S34 UAC + S33 EmployeeProfile
        //    precedent.
        if (effectiveFrom < predecessor.EffectiveFrom)
        {
            throw new InvalidProfileSupersessionException(
                $"Cannot supersede role config override for " +
                $"(employment_category='{employmentCategory}', agreement_code='{agreementCode}', " +
                $"ok_version='{okVersion}') with effective_from {effectiveFrom:yyyy-MM-dd} earlier than " +
                $"predecessor's effective_from {predecessor.EffectiveFrom:yyyy-MM-dd}.");
        }

        // 5. Case B — same-day edit. UPDATE-in-place with version bump.
        if (effectiveFrom == predecessor.EffectiveFrom)
        {
            var (sameDayOverrideId, sameDayVersion) = await UpdateInPlaceAsync(
                conn, tx, predecessor.OverrideId, newFields, ct);
            return new SaveRoleConfigOverrideResult(
                sameDayOverrideId, sameDayVersion, SaveOutcome.UpdatedInPlace);
        }

        // 6. Case C — cross-day edit. Close the predecessor at end-exclusive
        //    effective_to = effectiveFrom (version UNCHANGED — close is lifecycle,
        //    not a content edit), then INSERT new live row at predecessor.Version + 1
        //    per S33 / S34 Step 7a P1 ETag-monotonicity refinement (composite single-row
        //    natural key forces the version to carry the monotonic load alone — see
        //    InsertLiveRowAsync xmldoc).
        await ClosePredecessorAsync(conn, tx, predecessor.OverrideId, effectiveFrom, ct);
        var (supersedingOverrideId, supersedingVersion) = await InsertLiveRowAsync(
            conn, tx,
            employmentCategory, agreementCode, okVersion, newFields,
            effectiveFrom, actorId, actorRole,
            nextVersion: predecessor.Version + 1, ct);
        return new SaveRoleConfigOverrideResult(
            supersedingOverrideId, supersedingVersion, SaveOutcome.Superseded);
    }

    /// <summary>
    /// S40 / TASK-4003 — ADR-023 D8 soft-delete the live role config override row by
    /// stamping <c>effective_to = NOW()::date</c> under end-exclusive
    /// <c>[from, to)</c> semantics (ADR-018 D9). After this call, the row no longer
    /// satisfies the partial-unique-index <c>idx_role_config_overrides_live</c>
    /// predicate (<c>WHERE effective_to IS NULL</c>) and is invisible to
    /// <see cref="GetCurrentAsync(string, string, string, CancellationToken)"/>, but
    /// remains in the history table for replay determinism (ADR-016 D10).
    ///
    /// <para>
    /// <b>Predecessor <c>version</c> column is UNCHANGED (ADR-023 D8).</b> Soft-delete
    /// is a row-state-change, not a field-mutation: the row "disappears" from live
    /// reads via the partial-unique-index predicate, so bumping <c>version</c> would
    /// be redundant. The audit row emitted by the endpoint (S41) accordingly records
    /// <c>version_before = version_after = predecessor.version</c> per ADR-019 D8 for
    /// SOFT_DELETED actions — mirrors S33 EmployeeProfileRepository semantics, with
    /// the deliberate divergence from sibling ADR-019 D8 endpoints
    /// (<c>agreement_configs</c>, <c>wage_type_mappings</c>,
    /// <c>entitlement_configs</c>) which all bump version + 1 on soft-delete.
    /// </para>
    ///
    /// <para>
    /// <b>404-vs-412 retry semantic divergence.</b> Because the predecessor row's
    /// version is unchanged, an admin retry with stale
    /// <c>If-Match: "@expectedVersion"</c> after a successful soft-delete will hit
    /// <b>404 Not Found</b> (the partial-unique-index <c>WHERE effective_to IS NULL</c>
    /// matches no live row), <b>NOT 412 Precondition Failed</b>. This is intentional —
    /// soft-delete is idempotent-by-row-disappearance rather than
    /// idempotent-by-version-bump.
    /// </para>
    ///
    /// <para>
    /// <b>Atomic-outbox contract (ADR-018 D5).</b> Caller (S41 endpoint) owns the
    /// transaction; this method only writes to <c>role_config_overrides</c>. The
    /// endpoint emits the audit row (with
    /// <c>version_before = version_after = predecessor.version</c>) + outbox event in
    /// the same tx after this returns.
    /// </para>
    /// </summary>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when a live row exists for the natural-key triple but its
    /// <c>version</c> column differs from <paramref name="expectedVersion"/>.
    /// Endpoint maps to 412 Precondition Failed per ADR-019 D2.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no live row (<c>effective_to IS NULL</c>) exists for the
    /// natural-key triple. Endpoint maps to 404 Not Found. This is also the branch
    /// hit by an admin retry with stale <c>If-Match</c> after a successful
    /// soft-delete (the row "disappeared" from live reads per the partial-unique-
    /// index predicate).
    /// </exception>
    public async Task SoftDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employmentCategory, string agreementCode, string okVersion,
        long expectedVersion,
        string actorId, string actorRole,
        CancellationToken ct = default)
    {
        // 1. Single-statement UPDATE with row-disappearance semantic — no version bump
        //    (ADR-023 D8). The AND version = @expectedVersion predicate enforces
        //    optimistic concurrency without needing a separate SELECT ... FOR UPDATE
        //    step — unlike SupersedeAndCreateAsync's 3-case routing, soft-delete has
        //    no branching that needs the lock to be held across multiple statements.
        //    NOW()::date pins the close-stamp to day-granularity (effective_to is a
        //    DATE column per the S40 schema).
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE role_config_overrides
               SET effective_to = NOW()::date
             WHERE employment_category = @employmentCategory
               AND agreement_code = @agreementCode
               AND ok_version = @okVersion
               AND effective_to IS NULL
               AND version = @expectedVersion
            RETURNING override_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("employmentCategory", employmentCategory);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            // Happy path: UPDATE matched exactly one row (partial-unique-index
            // guarantees ≤1). Returned version is UNCHANGED from predecessor per
            // ADR-023 D8 — actor metadata is unused by the write itself but kept on
            // the signature so the endpoint passes through a consistent argument
            // shape across SupersedeAndCreateAsync / SoftDeleteAsync.
            _ = actorId;
            _ = actorRole;
            return;
        }
        // The reader must be disposed before we can issue the probe SELECT on the
        // same connection (Npgsql forbids overlapping commands on a single
        // connection).
        await reader.DisposeAsync();

        // 2. UPDATE matched no row. Probe to distinguish 404 (no live row) from 412
        //    (live row exists, version differs) per S33 SoftDeleteAsync precedent.
        //    This second read sits inside the same tx so it sees the same snapshot
        //    as the failed UPDATE — no chance of a TOCTOU window mis-classifying a
        //    concurrent insert as a 404.
        await using var probeCmd = new NpgsqlCommand(
            """
            SELECT version FROM role_config_overrides
            WHERE employment_category = @employmentCategory
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            """, conn, tx);
        probeCmd.Parameters.AddWithValue("employmentCategory", employmentCategory);
        probeCmd.Parameters.AddWithValue("agreementCode", agreementCode);
        probeCmd.Parameters.AddWithValue("okVersion", okVersion);
        var probeResult = await probeCmd.ExecuteScalarAsync(ct);
        if (probeResult is null || probeResult is DBNull)
        {
            // No live row → 404. This branch is also hit by an admin retry with
            // stale If-Match after a successful soft-delete (row disappeared per
            // partial-unique-index predicate; ADR-023 D8 row-disappearance
            // idempotency).
            throw new KeyNotFoundException(
                $"Role config override not found for " +
                $"(employment_category='{employmentCategory}', " +
                $"agreement_code='{agreementCode}', ok_version='{okVersion}').");
        }
        var actualVersion = (long)probeResult;
        // Live row exists but version differs → 412 per ADR-019 D2 admin-strict
        // If-Match.
        throw new OptimisticConcurrencyException(
            $"Role config override version is {actualVersion}, but caller sent " +
            $"If-Match: \"{expectedVersion}\"; refresh and retry.",
            expectedVersion: expectedVersion,
            actualVersion: actualVersion);
    }

    // ------------------------------------------------------------------
    // Audit — S24 Pattern B audit-bearing repository overload. Mirrors S25
    // AgreementConfigRepository.AppendAuditAsync v3 shape: writes
    // version_before / version_after columns per ADR-019 D8.
    // ------------------------------------------------------------------

    /// <summary>
    /// S40 / TASK-4003 — in-transaction audit insert (ADR-018 D5 atomic-outbox
    /// primitive + ADR-019 D8 version-transition columns). Mirrors S25
    /// AgreementConfigRepository.AppendAuditAsync v3 shape. The S41 endpoint composes
    /// the JSON snapshots (<paramref name="previousData"/> / <paramref name="newData"/>)
    /// and the version-transition pair, threading the call through the same tx as
    /// the write — this repository's <see cref="SupersedeAndCreateAsync"/> and
    /// <see cref="SoftDeleteAsync"/> do NOT write audit rows themselves per ADR-019
    /// D8 "endpoint owns audit emission".
    ///
    /// <para>
    /// <b>Action values</b> per the schema CHECK at <c>init.sql:1918</c>:
    /// <c>CREATED</c>, <c>UPDATED</c>, <c>SUPERSEDED</c>, <c>SOFT_DELETED</c>. The
    /// endpoint picks one based on the <see cref="SaveOutcome"/> returned from
    /// <see cref="SupersedeAndCreateAsync"/> (Created → CREATED, UpdatedInPlace →
    /// UPDATED, Superseded → SUPERSEDED) or the call site
    /// (<see cref="SoftDeleteAsync"/> → SOFT_DELETED).
    /// </para>
    ///
    /// <para>
    /// <b>Version-transition contract</b> per ADR-019 D8:
    /// <list type="bullet">
    ///   <item><description><c>CREATED</c> → <c>versionBefore = null</c>,
    ///     <c>versionAfter = 1</c>.</description></item>
    ///   <item><description><c>UPDATED</c> → <c>versionBefore = prior</c>,
    ///     <c>versionAfter = prior + 1</c>.</description></item>
    ///   <item><description><c>SUPERSEDED</c> → <c>versionBefore = predecessor.Version</c>,
    ///     <c>versionAfter = predecessor.Version + 1</c> (the successor row's
    ///     version).</description></item>
    ///   <item><description><c>SOFT_DELETED</c> →
    ///     <c>versionBefore = versionAfter = predecessor.Version</c> per ADR-023 D8
    ///     row-disappearance idempotency.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    // Step 7a Codex WARNING absorption (S40 close): AgreementConfigRepository
    // exposes 3 AppendAuditAsync overloads matching its 5 action types
    // (CREATED / UPDATED / PUBLISHED / ARCHIVED / CLONED) which differ on
    // which fields make sense per action. Our 4 actions (CREATED / UPDATED /
    // SUPERSEDED / SOFT_DELETED) all follow the same shape — action +
    // version_before + version_after + previous_data + new_data +
    // (actor_id, actor_role) — so a single overload suffices. S41 cutover
    // endpoint emitters call this method directly and pass NULL for unused
    // version-transition fields on the first INSERT case per ADR-019 D8
    // convention. Expanding to a 3-overload trio when no caller needs the
    // distinction would be ceremony without value.
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, string action,
        long? versionBefore, long? versionAfter,
        JsonElement? previousData, JsonElement? newData,
        string actorId, string actorRole,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_config_override_audit
                (override_id, action, version_before, version_after,
                 previous_data, new_data, actor_id, actor_role)
            VALUES
                (@overrideId, @action, @versionBefore, @versionAfter,
                 @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionAfter", (object?)versionAfter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("previousData", SerializeJsonElement(previousData));
        cmd.Parameters.AddWithValue("newData", SerializeJsonElement(newData));
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ------------------------------------------------------------------
    // Private helpers — shared by SupersedeAndCreateAsync's three routing branches.
    // Mirrors S34 UAC + S33 EmployeeProfile + S29 WTM AcquireLockAsync /
    // InsertLiveRowAsync / UpdateInPlaceAsync / ClosePredecessorAsync triad.
    // ------------------------------------------------------------------

    /// <summary>
    /// Locks the live row (<c>effective_to IS NULL</c>) for the natural-key triple
    /// via <c>SELECT ... FOR UPDATE</c>. Returns the locked row's <c>override_id</c>,
    /// current <c>version</c>, and <c>effective_from</c> — the three pieces of state
    /// <see cref="SupersedeAndCreateAsync"/> needs to route Cases A/B/C and validate
    /// optimistic concurrency. Returns <c>null</c> when no live row exists (Case A).
    /// </summary>
    private static async Task<(Guid OverrideId, long Version, DateOnly EffectiveFrom)?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employmentCategory, string agreementCode, string okVersion,
        CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT override_id, version, effective_from
            FROM role_config_overrides
            WHERE employment_category = @employmentCategory
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("employmentCategory", employmentCategory);
        lockCmd.Parameters.AddWithValue("agreementCode", agreementCode);
        lockCmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (
            reader.GetGuid(0),
            reader.GetInt64(1),
            reader.GetFieldValue<DateOnly>(2));
    }

    /// <summary>
    /// Case A (Create) + Case C (Supersede) shared path — INSERT a fresh live row
    /// with the caller-supplied <c>effective_from</c>. <c>override_id</c> is
    /// generated client-side so the endpoint can include it in the outbox event
    /// body. The partial-unique-index <c>idx_role_config_overrides_live</c>
    /// guarantees at most one open row per natural-key triple; in Case C the caller
    /// has already closed the predecessor under the same tx.
    ///
    /// <para>
    /// <b>Version contract (S33 / S34 Step 7a P1 absorption — ETag monotonicity).</b>
    /// Case A passes <paramref name="nextVersion"/> = 1 (no predecessor exists).
    /// Case C passes <paramref name="nextVersion"/> = <c>predecessor.Version + 1</c>
    /// so the admin's response ETag strictly increases across the supersession.
    /// </para>
    /// </summary>
    private static async Task<(Guid OverrideId, long Version)> InsertLiveRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employmentCategory, string agreementCode, string okVersion,
        RoleConfigOverrideFields fields,
        DateOnly effectiveFrom, string actorId, string actorRole,
        long nextVersion, CancellationToken ct)
    {
        var newOverrideId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_config_overrides (
                override_id, employment_category, agreement_code, ok_version,
                effective_from, effective_to, version,
                merarbejde_compensation_right,
                has_merarbejde, has_overtime,
                has_evening_supplement, has_night_supplement,
                has_weekend_supplement, has_holiday_supplement,
                max_flex_balance, flex_carryover_max,
                norm_period_weeks, weekly_norm_hours,
                created_by, created_by_role)
            VALUES (
                @overrideId, @employmentCategory, @agreementCode, @okVersion,
                @effectiveFrom, NULL, @version,
                @merarbejdeCompensationRight,
                @hasMerarbejde, @hasOvertime,
                @hasEveningSupplement, @hasNightSupplement,
                @hasWeekendSupplement, @hasHolidaySupplement,
                @maxFlexBalance, @flexCarryoverMax,
                @normPeriodWeeks, @weeklyNormHours,
                @createdBy, @createdByRole)
            RETURNING override_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("overrideId", newOverrideId);
        cmd.Parameters.AddWithValue("employmentCategory", employmentCategory);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("effectiveFrom", effectiveFrom);
        cmd.Parameters.AddWithValue("version", nextVersion);
        AddFieldParameters(cmd, fields);
        cmd.Parameters.AddWithValue("createdBy", actorId);
        cmd.Parameters.AddWithValue("createdByRole", actorRole);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING always yields one row on success.
            throw new InvalidOperationException(
                $"InsertLiveRowAsync produced no row for " +
                $"(employment_category='{employmentCategory}', agreement_code='{agreementCode}', " +
                $"ok_version='{okVersion}') at effective_from='{effectiveFrom:yyyy-MM-dd}'.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case B (UpdatedInPlace) — same-day UPDATE-in-place. Targets the (still-locked)
    /// live row by its <paramref name="overrideId"/>; refreshes the 11 mutable
    /// fields and bumps <c>version = version + 1</c>; <c>override_id</c>,
    /// natural-key triple, <c>effective_from</c>, and audit metadata are immutable
    /// across same-day edits (the original creator's identity stays on the row).
    /// </summary>
    private static async Task<(Guid OverrideId, long Version)> UpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, RoleConfigOverrideFields fields, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE role_config_overrides SET
                merarbejde_compensation_right = @merarbejdeCompensationRight,
                has_merarbejde = @hasMerarbejde,
                has_overtime = @hasOvertime,
                has_evening_supplement = @hasEveningSupplement,
                has_night_supplement = @hasNightSupplement,
                has_weekend_supplement = @hasWeekendSupplement,
                has_holiday_supplement = @hasHolidaySupplement,
                max_flex_balance = @maxFlexBalance,
                flex_carryover_max = @flexCarryoverMax,
                norm_period_weeks = @normPeriodWeeks,
                weekly_norm_hours = @weeklyNormHours,
                version = version + 1
            WHERE override_id = @overrideId
            RETURNING override_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        AddFieldParameters(cmd, fields);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"UpdateInPlaceAsync produced no row for override_id='{overrideId}'; " +
                "FOR UPDATE invariant violated.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case C (Supersede) — close the predecessor by stamping
    /// <c>effective_to = closeDate</c> under end-exclusive semantics (ADR-018 D9 —
    /// predecessor's history window becomes
    /// <c>[predecessor.effective_from, closeDate)</c>). The version column is NOT
    /// bumped: close is a lifecycle event, not a content edit (mirrors S34 UAC + S33
    /// EmployeeProfile + S29 WTM CloseRowAsync). Caller must already hold the row
    /// lock acquired via <see cref="AcquireLockAsync"/>.
    /// </summary>
    private static async Task ClosePredecessorAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, DateOnly closeDate, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            "UPDATE role_config_overrides SET effective_to = @closeDate WHERE override_id = @overrideId",
            conn, tx);
        closeCmd.Parameters.AddWithValue("closeDate", closeDate);
        closeCmd.Parameters.AddWithValue("overrideId", overrideId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddFieldParameters(NpgsqlCommand cmd, RoleConfigOverrideFields fields)
    {
        cmd.Parameters.AddWithValue(
            "merarbejdeCompensationRight",
            (object?)fields.MerarbejdeCompensationRight ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hasMerarbejde", (object?)fields.HasMerarbejde ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hasOvertime", (object?)fields.HasOvertime ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "hasEveningSupplement", (object?)fields.HasEveningSupplement ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "hasNightSupplement", (object?)fields.HasNightSupplement ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "hasWeekendSupplement", (object?)fields.HasWeekendSupplement ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "hasHolidaySupplement", (object?)fields.HasHolidaySupplement ?? DBNull.Value);
        cmd.Parameters.AddWithValue("maxFlexBalance", (object?)fields.MaxFlexBalance ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "flexCarryoverMax", (object?)fields.FlexCarryoverMax ?? DBNull.Value);
        cmd.Parameters.AddWithValue("normPeriodWeeks", (object?)fields.NormPeriodWeeks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("weeklyNormHours", (object?)fields.WeeklyNormHours ?? DBNull.Value);
    }

    private static object SerializeJsonElement(JsonElement? element)
    {
        // Audit JSONB columns accept null or a serialized JSON string. The endpoint
        // (S41) hands a JsonElement so the repository's signature stays type-safe at
        // the call site (rather than the AgreementConfig precedent's `string?`
        // shape); we re-serialize to the raw JSON text the @::jsonb cast expects.
        if (element is null) return DBNull.Value;
        return JsonSerializer.Serialize(element.Value);
    }

    private static RoleConfigOverride ReadEntity(NpgsqlDataReader reader) => new()
    {
        OverrideId = reader.GetGuid(reader.GetOrdinal("override_id")),
        EmploymentCategory = reader.GetString(reader.GetOrdinal("employment_category")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        EffectiveFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
            ? null
            : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_to")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        MerarbejdeCompensationRight = reader.IsDBNull(reader.GetOrdinal("merarbejde_compensation_right"))
            ? null
            : reader.GetString(reader.GetOrdinal("merarbejde_compensation_right")),
        HasMerarbejde = reader.IsDBNull(reader.GetOrdinal("has_merarbejde"))
            ? null
            : reader.GetBoolean(reader.GetOrdinal("has_merarbejde")),
        HasOvertime = reader.IsDBNull(reader.GetOrdinal("has_overtime"))
            ? null
            : reader.GetBoolean(reader.GetOrdinal("has_overtime")),
        HasEveningSupplement = reader.IsDBNull(reader.GetOrdinal("has_evening_supplement"))
            ? null
            : reader.GetBoolean(reader.GetOrdinal("has_evening_supplement")),
        HasNightSupplement = reader.IsDBNull(reader.GetOrdinal("has_night_supplement"))
            ? null
            : reader.GetBoolean(reader.GetOrdinal("has_night_supplement")),
        HasWeekendSupplement = reader.IsDBNull(reader.GetOrdinal("has_weekend_supplement"))
            ? null
            : reader.GetBoolean(reader.GetOrdinal("has_weekend_supplement")),
        HasHolidaySupplement = reader.IsDBNull(reader.GetOrdinal("has_holiday_supplement"))
            ? null
            : reader.GetBoolean(reader.GetOrdinal("has_holiday_supplement")),
        MaxFlexBalance = reader.IsDBNull(reader.GetOrdinal("max_flex_balance"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("max_flex_balance")),
        FlexCarryoverMax = reader.IsDBNull(reader.GetOrdinal("flex_carryover_max"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("flex_carryover_max")),
        NormPeriodWeeks = reader.IsDBNull(reader.GetOrdinal("norm_period_weeks"))
            ? null
            : reader.GetInt32(reader.GetOrdinal("norm_period_weeks")),
        WeeklyNormHours = reader.IsDBNull(reader.GetOrdinal("weekly_norm_hours"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("weekly_norm_hours")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedByRole = reader.GetString(reader.GetOrdinal("created_by_role")),
    };
}

// ------------------------------------------------------------------
// Result records — colocated with the repository per S33 EmployeeProfile + S34 UAC
// + S29 WTM precedent.
// ------------------------------------------------------------------

/// <summary>
/// S40 / TASK-4003 — result of
/// <see cref="RoleConfigOverrideRepository.SupersedeAndCreateAsync"/>.
/// <see cref="Outcome"/> discriminates which of the ADR-020 D2 3-case branches fired
/// so the endpoint (S41) can emit the correct event type and stamp the right audit
/// <c>action</c> column (CREATED / UPDATED / SUPERSEDED).
/// </summary>
/// <param name="OverrideId">The <c>override_id</c> of the row this call produced. In
/// Case A (Created) and Case C (Superseded) this is a freshly-generated UUID for the
/// new live row; in Case B (UpdatedInPlace) it is the predecessor's unchanged
/// <c>override_id</c>.</param>
/// <param name="Version">The post-write <c>version</c> column value on the row
/// identified by <see cref="OverrideId"/>. Case A → 1; Case B → <c>prior + 1</c>;
/// Case C → <c>predecessor.Version + 1</c> per S33 / S34 Step 7a P1 ETag-monotonicity
/// refinement.</param>
/// <param name="Outcome">Which ADR-020 D2 branch the call routed through.</param>
public sealed record SaveRoleConfigOverrideResult(
    Guid OverrideId,
    long Version,
    SaveOutcome Outcome);

/// <summary>
/// S40 / TASK-4003 — ADR-020 D2 3-case routing discriminator. Read by S41 endpoint
/// to map each case to its correct outbox event type and audit action.
/// </summary>
public enum SaveOutcome
{
    /// <summary>Case A — no live row existed; INSERT produced a brand-new live row at
    /// version=1.</summary>
    Created,

    /// <summary>Case B — live row existed and its <c>effective_from</c> matched the
    /// request's; UPDATE-in-place with version bump (<c>override_id</c> and
    /// <c>effective_from</c> unchanged).</summary>
    UpdatedInPlace,

    /// <summary>Case C — live row existed at an earlier <c>effective_from</c>;
    /// predecessor closed at end-exclusive
    /// <c>effective_to = request.EffectiveFrom</c> (version unchanged), new live row
    /// inserted at <c>predecessor.Version + 1</c>.</summary>
    Superseded,
}
