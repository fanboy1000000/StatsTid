using System.Data.Common;
using StatsTid.Auth;
using StatsTid.Infrastructure.Security;

namespace StatsTid.Backend.Api.Endpoints.Helpers;

/// <summary>
/// SEC-062 — the shared refusal of the organisation-drift guard.
///
/// <para><b>What the guard closes.</b> A write handler's scope check reads the person's
/// organisation on its own connection, before the handler locks anything. A transfer that commits
/// between that read and the handler's lock would let the write land on a person the actor was
/// never checked for. So each such handler keeps the organisation its check authorised (the
/// <c>…WithOrgAsync</c> validator siblings return it; the user-unit PUT keeps the pre-read its
/// organisation check used) and, once it holds a lock a transfer must also wait for (the
/// reporting-tree advisory the transfer takes too, or the person's <c>users</c> row
/// <c>FOR UPDATE</c>), compares it, ordinal, with the person's organisation as that lock found it.
/// The compare stays inline at each site, where its condition is decided (an arm flag on the
/// approval decisions, "not the actor's own data" where a person may act on their own); a
/// difference, or a null authorised organisation, ends here.</para>
///
/// <para><b>The answer.</b> The transaction rolls back, one WARNING names the clause
/// <see cref="OrgScopeValidator.OrganisationDriftClauseName"/>, and the request answers 409 with
/// exactly <see cref="OrgScopeValidator.OrganisationDriftReason"/>. Neither the body nor the log
/// carries an organisation id or a date (ADR-040 D7). A retry meets the ordinary scope check.</para>
/// </summary>
internal static class OrganisationDriftGuard
{
    /// <summary>The log category every site's WARNING is written under, so the refusals are
    /// greppable in one place.</summary>
    internal const string LogCategory = "StatsTid.Backend.Api.Endpoints.OrganisationDrift";

    /// <summary>
    /// Rolls <paramref name="tx"/> back, writes the WARNING and returns the 409. The caller has
    /// decided to refuse; nothing it wrote in <paramref name="tx"/> survives.
    /// </summary>
    public static async Task<IResult> RefuseAsync(
        DbTransaction tx, ILoggerFactory loggerFactory, ActorContext actor, string route, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(actor);

        await tx.RollbackAsync(ct);

        loggerFactory.CreateLogger(LogCategory).LogWarning(
            "Conflict ({Clause}): Actor={ActorId} Role={ActorRole} Route={Route} CorrelationId={CorrelationId}",
            OrgScopeValidator.OrganisationDriftClauseName, actor.ActorId, actor.ActorRole, route, actor.CorrelationId);

        return Results.Conflict(new { error = OrgScopeValidator.OrganisationDriftReason });
    }
}
