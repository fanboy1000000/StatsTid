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
    public string UserId { get; }

    public ConcurrentSeedConflictException(string userId)
        : base($"User agreement-code assignment for user_id='{userId}' lost a concurrent-create race (unique-index 23505); the live row exists. Refresh and retry.")
    {
        UserId = userId;
    }
}
