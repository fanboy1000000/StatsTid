using System.Data;
using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Repository for <see cref="LocalAgreementProfile"/> rows in <c>local_agreement_profiles</c>.
///
/// Per ADR-017 D2 / D2.1: lifecycle is <c>effective_to</c>-only (no <c>is_active</c>). At most
/// one open-ended profile per (org_id, agreement_code, ok_version) is enforced at the schema
/// level by the partial-unique-index <c>uq_local_agreement_profile_active</c>
/// WHERE effective_to IS NULL. Save = close-then-insert inside a single
/// <see cref="IsolationLevel.RepeatableRead"/> transaction with <c>SELECT ... FOR UPDATE</c>
/// on the current open row to gate concurrent writers (ETag/If-Match concurrency).
///
/// Pattern follows <see cref="LocalConfigurationRepository"/>: read methods open their own
/// connection via the injected <see cref="DbConnectionFactory"/>. Writes have two flavors:
/// <list type="bullet">
/// <item><description>Self-contained:
/// <see cref="SupersedeAndCreateAsync(System.Nullable{System.Guid}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// owns its own connection and transaction. Returns the new profile id on success.</description></item>
/// <item><description>In-transaction sibling:
/// <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{System.Guid}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// reuses a caller-supplied connection + transaction so the caller can extend the same
/// PostgreSQL transaction across event-store + audit-row writes (ADR-017 D6 transactional
/// contract — no two-phase commit across stores; same database).</description></item>
/// </list>
///
/// This repository is a pure CRUD facade. It does NOT emit domain events or write audit rows;
/// the calling service (PUT endpoint, TASK-2107) is responsible for coordinating event-store
/// + audit writes alongside the profile mutation.
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
              AND (effective_to IS NULL OR effective_to >= @periodStart)
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
    /// Atomically supersedes the currently-active profile (if any) and inserts
    /// <paramref name="newProfile"/> as the new currently-active profile. Self-contained
    /// overload: opens its own connection and transaction. This is the ETag/If-Match
    /// concurrency entry point per ADR-017 D2.1 for callers that do not need to extend
    /// the transaction across additional writes.
    ///
    /// For cross-store atomicity (event-store + audit + profile in a single transaction —
    /// ADR-017 D6 transactional contract), use the in-transaction sibling overload
    /// <see cref="SupersedeAndCreateAsync(NpgsqlConnection, NpgsqlTransaction, System.Nullable{System.Guid}, LocalAgreementProfile, System.Threading.CancellationToken)"/>.
    ///
    /// Concurrency contract:
    /// <list type="bullet">
    /// <item><description><paramref name="expectedCurrentProfileId"/> = <c>null</c> means the
    /// caller asserted "no current profile exists" (HTTP <c>If-None-Match: *</c> for first
    /// creation). If the lock SELECT finds a row, <see cref="OptimisticConcurrencyException"/>
    /// is thrown with <see cref="OptimisticConcurrencyException.ActualProfileId"/> populated.</description></item>
    /// <item><description><paramref name="expectedCurrentProfileId"/> non-null means the caller
    /// asserted that profile is the current open one (HTTP <c>If-Match: &lt;id&gt;</c>). The
    /// lock SELECT must return exactly that profile_id; mismatch (including null = "now no
    /// current open") throws <see cref="OptimisticConcurrencyException"/>.</description></item>
    /// </list>
    ///
    /// Transaction: <see cref="IsolationLevel.RepeatableRead"/>. The
    /// <c>SELECT ... FOR UPDATE</c> on the partial-unique row serializes concurrent writers to
    /// the same (org, agreement_code, ok_version) triple. PostgreSQL RepeatableRead is
    /// sufficient here — it prevents non-repeatable reads and the row-lock blocks competitors.
    /// </summary>
    /// <returns>The new profile's <see cref="Guid"/>.</returns>
    /// <exception cref="OptimisticConcurrencyException">If the precondition encoded in
    /// <paramref name="expectedCurrentProfileId"/> does not match the row currently holding
    /// the active slot.</exception>
    public async Task<Guid> SupersedeAndCreateAsync(
        Guid? expectedCurrentProfileId, LocalAgreementProfile newProfile, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        try
        {
            var newProfileId = await SupersedeAndCreateAsync(conn, tx, expectedCurrentProfileId, newProfile, ct);
            await tx.CommitAsync(ct);
            return newProfileId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="SupersedeAndCreateAsync(System.Nullable{System.Guid}, LocalAgreementProfile, System.Threading.CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> and <paramref name="tx"/> so the
    /// caller can extend the same PostgreSQL transaction across event-store + audit-row writes
    /// (ADR-017 D6 transactional contract). The caller is responsible for committing or rolling
    /// back the transaction; this method does NOT commit and does NOT rollback.
    ///
    /// Concurrency semantics, isolation expectation, and exception contract are identical to
    /// the self-contained overload — the caller should open the transaction with
    /// <see cref="IsolationLevel.RepeatableRead"/> for equivalent guarantees.
    /// </summary>
    /// <returns>The new profile's <see cref="Guid"/>.</returns>
    /// <exception cref="OptimisticConcurrencyException">If the precondition encoded in
    /// <paramref name="expectedCurrentProfileId"/> does not match the row currently holding
    /// the active slot.</exception>
    public async Task<Guid> SupersedeAndCreateAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid? expectedCurrentProfileId,
        LocalAgreementProfile newProfile,
        CancellationToken ct = default)
    {
        // 1. Lock the currently-active row (if any) so concurrent supersessions serialize here.
        var actualCurrentProfileId = await AcquireLockAsync(
            conn, tx, newProfile.OrgId, newProfile.AgreementCode, newProfile.OkVersion, ct);

        // 2. Validate the precondition (If-None-Match: * vs If-Match: <id>).
        ValidatePrecondition(actualCurrentProfileId, expectedCurrentProfileId);

        // 3. Close the predecessor (if any) by setting effective_to = today.
        // "Today" is computed in UTC. Admins in Europe/Copenhagen saving across
        // a UTC-midnight boundary may see effective_to stamped as "yesterday"
        // relative to their wall clock. Acceptable for S21; Phase-4 hardening
        // sub-sprint (per ADR-017 D2.2) revisits with a TimeProvider/IClock
        // injection uniformly across admin-write surfaces.
        if (expectedCurrentProfileId is not null)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            await CloseProfileAsync(conn, tx, expectedCurrentProfileId.Value, today, ct);
        }

        // 4. Insert the new currently-active profile. ProfileId is generated client-side
        //    (matches LocalConfigurationRepository.CreateAsync precedent).
        var newProfileId = newProfile.ProfileId == Guid.Empty ? Guid.NewGuid() : newProfile.ProfileId;
        await InsertProfileAsync(conn, tx, newProfile, newProfileId, ct);
        return newProfileId;
    }

    /// <summary>
    /// Acquires a row-level lock on the currently-active profile (if any) for the
    /// (org, agreement_code, ok_version) triple via <c>SELECT profile_id ... FOR UPDATE</c>.
    /// Returns the locked profile_id, or <c>null</c> if no profile is currently active.
    /// Concurrent writers attempting the same lock serialize on this query.
    /// </summary>
    private static async Task<Guid?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string orgId, string agreementCode, string okVersion,
        CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT profile_id FROM local_agreement_profiles
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
        if (await reader.ReadAsync(ct))
            return reader.GetGuid(0);
        return null;
    }

    /// <summary>
    /// Applies the optimistic-concurrency precondition (HTTP If-Match / If-None-Match)
    /// against the row actually present at lock time. Throws
    /// <see cref="OptimisticConcurrencyException"/> on mismatch with both the expected and
    /// actual ids surfaced in the exception (the PUT endpoint maps these to the 412 body).
    /// </summary>
    private static void ValidatePrecondition(Guid? actualCurrentProfileId, Guid? expectedCurrentProfileId)
    {
        if (expectedCurrentProfileId is null && actualCurrentProfileId is not null)
        {
            throw new OptimisticConcurrencyException(
                $"Cannot create: a current profile already exists ({actualCurrentProfileId.Value}); " +
                $"use If-Match: {actualCurrentProfileId.Value} for supersession.",
                expectedProfileId: null,
                actualProfileId: actualCurrentProfileId);
        }
        if (expectedCurrentProfileId is not null && actualCurrentProfileId != expectedCurrentProfileId)
        {
            throw new OptimisticConcurrencyException(
                $"Current profile is {(actualCurrentProfileId?.ToString() ?? "<none>")}, " +
                $"but caller sent If-Match: {expectedCurrentProfileId.Value}; refresh and retry.",
                expectedProfileId: expectedCurrentProfileId,
                actualProfileId: actualCurrentProfileId);
        }
    }

    /// <summary>
    /// Closes the supplied currently-active profile by stamping <c>effective_to</c> to
    /// <paramref name="today"/>. Caller must already hold the row lock acquired via
    /// <see cref="AcquireLockAsync"/>.
    /// </summary>
    private static async Task CloseProfileAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid currentProfileId, DateOnly today, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            "UPDATE local_agreement_profiles SET effective_to = @today WHERE profile_id = @currentProfileId",
            conn, tx);
        closeCmd.Parameters.AddWithValue("today", today);
        closeCmd.Parameters.AddWithValue("currentProfileId", currentProfileId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts a new profile row with the supplied <paramref name="newProfileId"/> as the
    /// currently-active profile (effective_to NULL). Assumes the active-slot lock has already
    /// been acquired and (where needed) the predecessor closed via <see cref="CloseProfileAsync"/>.
    /// </summary>
    private static async Task InsertProfileAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        LocalAgreementProfile newProfile, Guid newProfileId, CancellationToken ct)
    {
        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, flex_carryover_max,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by, created_at)
            VALUES (
                @profileId, @orgId, @agreementCode, @okVersion,
                @effectiveFrom, NULL,
                @weeklyNormHours, @maxFlexBalance, @flexCarryoverMax,
                @maxOvertimeHoursPerPeriod, @overtimeRequiresPreApproval,
                @createdBy, @createdAt)
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
        await insertCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Closes the currently-active profile (if any) by setting <c>effective_to = today</c>
    /// without inserting a successor. Returns the number of rows affected (0 = no current
    /// open profile, 1 = closed). Implements the deactivation-without-supersession case
    /// per ADR-017 D2.
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
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}

/// <summary>
/// Thrown by <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync(System.Nullable{System.Guid}, LocalAgreementProfile, System.Threading.CancellationToken)"/>
/// (and its in-transaction sibling) when the caller's optimistic-concurrency precondition
/// (HTTP <c>If-Match</c> / <c>If-None-Match: *</c>) does not match the row currently holding
/// the active slot. Per ADR-017 D2.1, the PUT endpoint (TASK-2107) maps this to
/// <c>412 Precondition Failed</c> and returns the current state in the response body.
/// </summary>
public sealed class OptimisticConcurrencyException : Exception
{
    /// <summary>The profile_id the caller asserted was current (null = "no current expected").</summary>
    public Guid? ExpectedProfileId { get; }

    /// <summary>The profile_id actually current at lock time (null = "no current open profile").</summary>
    public Guid? ActualProfileId { get; }

    public OptimisticConcurrencyException(string message)
        : base(message) { }

    public OptimisticConcurrencyException(string message, Guid? expectedProfileId, Guid? actualProfileId)
        : base(message)
    {
        ExpectedProfileId = expectedProfileId;
        ActualProfileId = actualProfileId;
    }

    public OptimisticConcurrencyException(string message, Exception innerException)
        : base(message, innerException) { }
}
