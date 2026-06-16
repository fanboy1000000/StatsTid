using StatsTid.Infrastructure;

namespace StatsTid.Backend.Api.Endpoints.Helpers;

/// <summary>
/// S78 R9 — the BOUNDED drift-retry wrapper shared by every employee-current-root tree-keyed mutator
/// (reporting-line assign / remove / acting + admin-vikar create in <c>ReportingLineEndpoints</c>, and the
/// cross-styrelse transfer <c>PUT /api/admin/users/{userId}</c> in <c>AdminEndpoints</c>).
///
/// <para>
/// The advisory key those mutators take derives from the mutable <c>users.primary_org_id</c> via an
/// unlocked read, so a concurrent cross-styrelse transfer can leave a mutator holding a STALE key.
/// <see cref="ReportingLineRepository.AcquireTreeLockForEmployeeAsync"/> (and the transfer variant
/// <see cref="ReportingLineRepository.AcquireTreeLocksForTransferAsync"/>) detect this by re-deriving the
/// root UNDER the held advisory and throwing <see cref="TreeRootDriftException"/>. Because
/// <c>pg_advisory_xact_lock</c> releases ONLY at tx end, the sound recovery is to ROLL BACK the whole
/// transaction (the drift check runs BEFORE any user-row <c>FOR UPDATE</c> or mutation, so a drifted
/// attempt has NO side effects) and RETRY the entire request body on a FRESH transaction.
/// </para>
///
/// <para>
/// <see cref="RunAsync"/> retries <see cref="MaxDriftRetries"/> times and, on exhaustion, returns a
/// PINNED 409 — never an incidental 5xx and never a hang. Three consecutive transfers racing the same
/// mutator is astronomically rare; a clean retryable 409 is the correct terminal.
/// </para>
/// </summary>
public static class TreeRootDriftRetry
{
    /// <summary>Bounded retry count (≤3 retries after the first attempt).</summary>
    public const int MaxDriftRetries = 3;

    /// <summary>
    /// Runs <paramref name="attempt"/> (one full request body opening its OWN connection + transaction and
    /// rolling back on any throw) and retries the WHOLE body on <see cref="TreeRootDriftException"/>.
    /// </summary>
    public static async Task<IResult> RunAsync(Func<Task<IResult>> attempt)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                return await attempt();
            }
            catch (TreeRootDriftException) when (i < MaxDriftRetries)
            {
                // The tree root drifted under the advisory (a concurrent transfer committed). The
                // attempt's tx already rolled back (no side effects). Re-derive from scratch on retry.
            }
            catch (TreeRootDriftException)
            {
                // Bounded retries exhausted — a PINNED, retryable 409 (not a 5xx / hang).
                return Results.Json(new
                {
                    error = "The employee's organisation changed concurrently during this operation; refresh and retry.",
                }, statusCode: 409);
            }
        }
    }
}
