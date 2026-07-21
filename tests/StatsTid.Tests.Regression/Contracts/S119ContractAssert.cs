using System.Net.Http;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S119 / TASK-11902 — tiny shared asserts for the Pass-6 per-route spec≡runtime classes
/// (<c>S119ConfigSpecRuntimeTests</c> / <c>S119ProfileSpecRuntimeTests</c> /
/// <c>S119ProjectSpecRuntimeTests</c>):
///
/// <list type="bullet">
///   <item><description><see cref="WithIfNoneMatchStar"/> — decorates a request with the
///     profile-flexible first-create precondition <c>If-None-Match: *</c> (ADR-018 D7,
///     the program's first live <c>ifNoneMatch</c> surface — the profile chain's create
///     path). Mutually exclusive with If-Match by the helper contract
///     (<c>EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar</c>).</description></item>
///   <item><description><see cref="AssertNoEtag"/> — the PRECONDITION-FREE family pin
///     (Step-0b Reviewer N1): the projects family has NO If-Match/ETag surface anywhere,
///     so its mutations must both SUCCEED without any precondition header (asserted by the
///     calling test sending none) and serve NO ETag header (asserted here). If the backend
///     ever grows a concurrency surface on this family, these pins go RED.</description></item>
/// </list>
///
/// <para><see cref="SpecRuntimeMatcher"/> + <see cref="SpecRuntimeTestSupport"/> +
/// <see cref="S118ContractAssert"/> (exact-key-set + ETag-version primitives) are consumed
/// AS-IS — this file only ADDS sibling helpers; no existing test file is modified.</para>
/// </summary>
internal static class S119ContractAssert
{
    /// <summary>Add <c>If-None-Match: *</c> (RFC 7232 first-creation precondition) to the
    /// request and return it (builder style, composes with
    /// <see cref="SpecRuntimeTestSupport.JsonRequest"/>).</summary>
    public static HttpRequestMessage WithIfNoneMatchStar(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        return request;
    }

    /// <summary>Assert the response carries NO ETag header — the projects-family
    /// precondition-free pin (the family has no concurrency surface by design).</summary>
    public static void AssertNoEtag(HttpResponseMessage response, string context)
    {
        if (response.Headers.ETag is not null)
            throw new XunitException(
                $"{context}: expected NO ETag header (the projects family is precondition-free " +
                $"by design) but got '{response.Headers.ETag.Tag}'.");
    }
}
