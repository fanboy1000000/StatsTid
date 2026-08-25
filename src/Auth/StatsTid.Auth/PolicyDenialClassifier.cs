using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace StatsTid.Auth;

/// <summary>
/// How a policy-authorization DENIAL should be recorded (QUAL-009 / SEC-038). The owner ruling
/// (OQ-1a) is: a structured LOG on EVERY denial, but an <c>audit_log</c> ROW only on the
/// sensitive routes — <b>ADMIN-STRICT or MUTATING</b>. This enum is the single classification the
/// result-handler (which picks the log level) and the audit-row middleware (which decides whether
/// to persist a row) both read, so the two can never disagree.
/// </summary>
public enum DenialRouteClass
{
    /// <summary>
    /// A read (GET/HEAD/OPTIONS/TRACE) on a route guarded by a NON-admin policy. These are the
    /// high-volume, low-signal "you're outside your scope" 403s — logged at <c>Information</c>,
    /// NO audit row. This value is only ever assigned when a route is <i>positively proven</i>
    /// routine; anything unproven falls through to <see cref="AdminOrMutating"/> (deny-by-default).
    /// </summary>
    RoutineRead,

    /// <summary>
    /// An admin-strict route, OR any state-changing method, OR an <b>unknown/unclassifiable</b>
    /// route. Logged at <c>Warning</c> and written as an <c>audit_log</c> row. This is the
    /// deny-by-default bucket: a brand-new route nobody has classified lands here, never silently
    /// in the log-only bucket.
    /// </summary>
    AdminOrMutating,
}

/// <summary>
/// Classifies a denied request into a <see cref="DenialRouteClass"/> from request + endpoint
/// metadata ONLY (never from claims/token/body). The rule is <b>deny-by-default</b>: a denial is
/// treated as routine (log-only) ONLY when it is provably a read against a known non-admin policy;
/// every other shape — mutating method, admin-strict policy, unnamed policy, or no matched
/// endpoint — is auditable.
/// </summary>
public static class PolicyDenialClassifier
{
    // The admin-strict policies are DELIBERATELY not enumerated here. Instead we enumerate the
    // SAFE set — the non-admin READ policies whose read-method denials are routine — and treat
    // everything else as auditable. That inversion is what makes the guard deny-by-default: adding
    // a new policy (or a new admin policy) without touching this file leaves it auditable, which is
    // the safe failure mode. The three names below are the current non-admin policies from
    // AuthorizationPolicies.AddStatsTidPolicies.
    private static readonly HashSet<string> RoutineReadPolicies = new(StringComparer.Ordinal)
    {
        "EmployeeOrAbove",
        "LeaderOrAbove",
        "Authenticated",
    };

    /// <summary>
    /// Returns <see cref="DenialRouteClass.RoutineRead"/> only when the request is a read method AND
    /// the matched endpoint's authorization is expressed ENTIRELY through known non-admin policies;
    /// otherwise <see cref="DenialRouteClass.AdminOrMutating"/> (the deny-by-default bucket).
    /// </summary>
    public static DenialRouteClass ClassifyRoute(HttpContext context)
    {
        // (1) Any state-changing method is auditable regardless of the endpoint's policy — a
        //     mutating request that got as far as an authorization denial is high-signal.
        if (!IsReadMethod(context.Request.Method))
            return DenialRouteClass.AdminOrMutating;

        // (2) No matched endpoint → we cannot prove it is routine → deny-by-default.
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
            return DenialRouteClass.AdminOrMutating;

        // (3) Collect the NAMED policies declared on the endpoint. A call that names a policy adds
        //     an IAuthorizeData whose Policy is that name; a parameterless authorize call has a null
        //     Policy and contributes nothing here.
        var policyNames = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        // (4) No NAMED policy to vouch for it → deny-by-default.
        if (policyNames.Count == 0)
            return DenialRouteClass.AdminOrMutating;

        // (5) Routine ONLY when EVERY named policy is a known non-admin read policy. A single
        //     admin-strict or unrecognized policy tips the whole route into auditable.
        return policyNames.All(RoutineReadPolicies.Contains)
            ? DenialRouteClass.RoutineRead
            : DenialRouteClass.AdminOrMutating;
    }

    /// <summary>
    /// A STABLE, non-PII identifier for the route's authorization — the comma-joined named policies
    /// (e.g. <c>"GlobalAdminOnly"</c>), or a sentinel when there is no endpoint / no named policy.
    /// Safe to log and to persist: it is server-defined and contains no claims or request data.
    /// </summary>
    public static string DescribePolicies(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
            return "(no-endpoint)";

        var names = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return names.Count == 0 ? "(unnamed-authorize)" : string.Join(",", names);
    }

    private static bool IsReadMethod(string method) =>
        HttpMethods.IsGet(method) ||
        HttpMethods.IsHead(method) ||
        HttpMethods.IsOptions(method) ||
        HttpMethods.IsTrace(method);
}
