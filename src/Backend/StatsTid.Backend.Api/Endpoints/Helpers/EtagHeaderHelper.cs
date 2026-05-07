using System.Globalization;

namespace StatsTid.Backend.Api.Endpoints.Helpers;

/// <summary>
/// Shared parser for HTTP ETag preconditions (<c>If-Match</c> / <c>If-None-Match</c>) per
/// RFC 7232. Lifted from the original <c>ConfigEndpoints.TryParseConcurrencyPrecondition</c>
/// (TASK-2502) so the same parsing logic can be reused across the admin-config surfaces
/// added by S25 Phase 4c Part 2 (D2.2 ETag/If-Match propagation per ADR-019 pending —
/// agreement_configs DRAFT edits, position_overrides, wage_type_mappings, entitlement_configs).
///
/// Two modes are exposed:
///   <list type="bullet">
///     <item>
///       <description>
///         <see cref="TryParseIfMatchOrIfNoneMatchStar"/> — profile-flexible mode used by the
///         <c>ConfigEndpoints</c> PUT handler (LocalAgreementProfile lifecycle). Accepts
///         <c>If-Match: "&lt;version&gt;"</c> for supersede / update-in-place AND
///         <c>If-None-Match: *</c> for first-creation.
///       </description>
///     </item>
///     <item>
///       <description>
///         <see cref="TryParseIfMatch"/> — admin-strict mode for new admin mutating endpoints
///         (S25 Phase 4c Part 2). Accepts ONLY <c>If-Match: "&lt;version&gt;"</c>. REJECTS
///         <c>If-None-Match: *</c> because admin endpoints have no first-create semantic
///         after the schema migration: rows are created via separate POST /create endpoints
///         that do NOT require any If-* header — they set ETag on the 201 response.
///       </description>
///     </item>
///   </list>
///
/// <para>
/// Wire format per RFC 7232: <c>If-Match: "&lt;version&gt;"</c> with the numeric version
/// quoted. Surrounding quotes and whitespace are tolerated; non-numeric bodies are rejected.
/// Bare-numeric (no quotes) is accepted defensively — same forgiveness as the original
/// helper to keep the seam byte-for-byte identical.
/// </para>
/// </summary>
public static class EtagHeaderHelper
{
    /// <summary>
    /// Profile-flexible mode used by <c>ConfigEndpoints</c> PUT — accepts both:
    ///   <list type="bullet">
    ///     <item><description><c>If-Match: "&lt;version&gt;"</c> (supersede / update existing).</description></item>
    ///     <item><description><c>If-None-Match: *</c> (first-create — no prior version).</description></item>
    ///   </list>
    /// Returns parsed <paramref name="expectedVersion"/> (<c>null</c> = first-create), or
    /// <c>false</c> if header is missing / malformed / both supplied / If-None-Match value is
    /// not exactly <c>*</c>.
    /// </summary>
    /// <param name="request">The incoming HTTP request whose headers are inspected.</param>
    /// <param name="expectedVersion">
    /// On success: the version asserted by If-Match (long), or <c>null</c> when If-None-Match: *
    /// signals first-creation. On failure: undefined (callers must check the bool return value).
    /// </param>
    /// <param name="errorMessage">
    /// On failure: a human-readable diagnostic suitable for surfacing in a 4xx error body.
    /// On success: <c>null</c>.
    /// </param>
    /// <returns><c>true</c> when the precondition parsed cleanly; <c>false</c> otherwise.</returns>
    public static bool TryParseIfMatchOrIfNoneMatchStar(
        HttpRequest request, out long? expectedVersion, out string? errorMessage)
    {
        expectedVersion = null;
        errorMessage = null;

        var ifMatch = request.Headers.IfMatch.ToString();
        var ifNoneMatch = request.Headers.IfNoneMatch.ToString();
        var hasIfMatch = !string.IsNullOrWhiteSpace(ifMatch);
        var hasIfNoneMatch = !string.IsNullOrWhiteSpace(ifNoneMatch);

        if (!hasIfMatch && !hasIfNoneMatch)
        {
            errorMessage = "Missing If-Match: \"<version>\" (for supersession or in-place edit) or If-None-Match: * (for first creation).";
            return false;
        }
        if (hasIfMatch && hasIfNoneMatch)
        {
            errorMessage = "Send exactly one of If-Match or If-None-Match — not both.";
            return false;
        }

        if (hasIfNoneMatch)
        {
            if (!ifNoneMatch.Trim().Equals("*", StringComparison.Ordinal))
            {
                errorMessage = "If-None-Match must be exactly '*' (first-creation precondition).";
                return false;
            }
            expectedVersion = null;
            return true;
        }

        // If-Match: "<version>" per RFC 7232. Strip surrounding quotes / whitespace; bare
        // numeric (unquoted) is accepted defensively.
        var raw = ifMatch.Trim().Trim('"');
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            errorMessage = $"If-Match header is not a valid version (expected RFC 7232 quoted long): '{ifMatch}'.";
            return false;
        }
        expectedVersion = parsed;
        return true;
    }

    /// <summary>
    /// Admin-strict mode for new admin mutating endpoints (S25 Phase 4c Part 2 / ADR-019
    /// pending). Accepts ONLY:
    ///   <list type="bullet">
    ///     <item><description><c>If-Match: "&lt;version&gt;"</c> (supersede / update existing).</description></item>
    ///   </list>
    /// REJECTS <c>If-None-Match: *</c> with a clear error — admin endpoints have no
    /// first-create semantic after the schema migration; rows are created via separate POST
    /// /create endpoints that don't require any If-* header (they set ETag on the 201
    /// response).
    /// </summary>
    /// <param name="request">The incoming HTTP request whose headers are inspected.</param>
    /// <param name="expectedVersion">
    /// On success: the version asserted by If-Match (long).
    /// On failure: <c>0</c> (callers must check the bool return value, not the out param).
    /// </param>
    /// <param name="errorMessage">
    /// On failure: a human-readable diagnostic suitable for surfacing in a 4xx error body.
    /// On success: <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> when <c>If-Match</c> parses cleanly; <c>false</c> if header missing /
    /// malformed / If-None-Match supplied (incorrect mode for admin-strict endpoints).
    /// </returns>
    public static bool TryParseIfMatch(
        HttpRequest request, out long expectedVersion, out string? errorMessage)
    {
        expectedVersion = 0;
        errorMessage = null;

        var ifMatch = request.Headers.IfMatch.ToString();
        var ifNoneMatch = request.Headers.IfNoneMatch.ToString();
        var hasIfMatch = !string.IsNullOrWhiteSpace(ifMatch);
        var hasIfNoneMatch = !string.IsNullOrWhiteSpace(ifNoneMatch);

        if (hasIfNoneMatch)
        {
            errorMessage = "If-None-Match is not accepted on this endpoint — admin mutating endpoints require If-Match: \"<version>\". Use POST /create for new rows.";
            return false;
        }
        if (!hasIfMatch)
        {
            errorMessage = "Missing If-Match: \"<version>\" precondition.";
            return false;
        }

        // If-Match: "<version>" per RFC 7232. Strip surrounding quotes / whitespace; bare
        // numeric (unquoted) is accepted defensively (same forgiveness as the flexible mode).
        var raw = ifMatch.Trim().Trim('"');
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            errorMessage = $"If-Match header is not a valid version (expected RFC 7232 quoted long): '{ifMatch}'.";
            return false;
        }
        expectedVersion = parsed;
        return true;
    }
}
