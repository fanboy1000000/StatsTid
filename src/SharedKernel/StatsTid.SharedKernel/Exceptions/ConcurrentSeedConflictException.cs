namespace StatsTid.SharedKernel.Exceptions;

/// <summary>
/// S35 / TASK-3502 — thrown by
/// <see cref="StatsTid.Infrastructure.UserAgreementCodeRepository.SupersedeAndCreateAsync"/>
/// when the Case A (no predecessor) INSERT loses a concurrent-create race on the
/// partial-unique-index <c>idx_user_agreement_codes_live</c>. Two concurrent admin
/// POST or admin PUT calls that both route through Case A — having each observed
/// "no live row exists" before the partial-unique-index serialized the writes —
/// can collide on the unique constraint; the loser surfaces <c>PostgresException
/// (SqlState="23505")</c>. The repository catches that 23505 and re-throws as this
/// typed exception so endpoints map it to a client-actionable status code.
///
/// <para>
/// <b>HTTP mapping — symmetric to <see cref="OptimisticConcurrencyException"/>.</b>
/// <see cref="OptimisticConcurrencyException"/> fires on version mismatch under
/// ADR-018 D7 row-version + If-Match optimistic concurrency and maps to
/// <b>412 Precondition Failed</b>. This exception fires on the structurally
/// distinct race where neither caller carries an If-Match (both are Case A) and
/// maps to <b>409 Conflict</b> per RFC 7232 §4.1 — the live row exists post-race,
/// so the caller's intended INSERT cannot succeed and the right answer is "refresh
/// and retry as Case B/C". See ADR-018 D7 for the row-version contract and
/// ADR-020 D2 for the 3-case routing (Case A is the only branch that can raise
/// this exception; Cases B/C update or supersede an existing row and cannot
/// trigger the partial-unique-index conflict).
/// </para>
///
/// <para>
/// <b>Callers.</b> <c>AdminEndpoints</c> POST <c>/api/admin/users</c> and PUT
/// <c>/api/admin/users/{userId}</c> map this exception to 409 Conflict with a
/// structured body carrying <c>userId</c> and a retry hint. The backfill seeder
/// (<c>UserAgreementCodeBackfillSeeder</c>) catches the underlying 23505
/// <em>inline</em> at the SQL boundary and swallows it as an idempotent skip —
/// it never raises this exception (see <c>UserAgreementCodeBackfillSeeder.cs</c>
/// L223 for the seeder pattern, which is correct for its bootstrap semantic where
/// the winner's row is, by construction, semantically identical).
/// </para>
/// </summary>
public sealed class ConcurrentSeedConflictException : InvalidOperationException
{
    /// <summary>
    /// The natural-key string that lost the concurrent-create race. For the original
    /// S35 caller (<c>UserAgreementCodeRepository</c>) this is the user_id; for the
    /// composite-key callers (S40 <c>RoleConfigOverrideRepository</c> et al.) this is
    /// the composite key serialized as a single string (see <see cref="ResourceType"/>
    /// for the originating table). The legacy <see cref="UserId"/> getter aliases this
    /// for the existing S35 endpoint catch sites.
    /// </summary>
    public string ResourceKey { get; }

    /// <summary>
    /// Identifies which versioned-config table raised the race — e.g.
    /// <c>"user_agreement_codes"</c> (S35) or <c>"role_config_overrides"</c> (S40).
    /// Endpoints use this to dispatch the right log-line / response body shape.
    /// </summary>
    public string ResourceType { get; }

    /// <summary>
    /// Legacy alias for <see cref="ResourceKey"/> preserved for the S35
    /// <c>UserAgreementCodeRepository</c> endpoint catch sites (AdminEndpoints
    /// L636 + L1230). Returns the same value as <see cref="ResourceKey"/>.
    /// </summary>
    public string UserId => ResourceKey;

    /// <summary>
    /// S35 original constructor — assumes the resource is a <c>user_agreement_codes</c>
    /// row keyed by user_id. Preserved unchanged for the existing AdminEndpoints
    /// callers; new repositories should use the
    /// <see cref="ConcurrentSeedConflictException(string, string)"/> overload.
    /// </summary>
    public ConcurrentSeedConflictException(string userId)
        : base($"User agreement-code assignment for user_id='{userId}' lost a concurrent-create race (unique-index 23505); the live row exists. Refresh and retry.")
    {
        ResourceKey = userId;
        ResourceType = "user_agreement_codes";
    }

    /// <summary>
    /// S40 / TASK-4003 generalized constructor — accepts an arbitrary
    /// <paramref name="resourceType"/> (table name) + <paramref name="resourceKey"/>
    /// (natural-key string, typically composite-keys joined with <c>'|'</c>). Used by
    /// <c>RoleConfigOverrideRepository.SupersedeAndCreateAsync</c> on Case A 23505
    /// races where the composite key
    /// <c>(employment_category, agreement_code, ok_version)</c> cannot fit the
    /// single-string user_id contract of the S35 constructor.
    /// </summary>
    public ConcurrentSeedConflictException(string resourceType, string resourceKey)
        : base($"{resourceType} row for key='{resourceKey}' lost a concurrent-create race (unique-index 23505); the live row exists. Refresh and retry.")
    {
        ResourceKey = resourceKey;
        ResourceType = resourceType;
    }
}
