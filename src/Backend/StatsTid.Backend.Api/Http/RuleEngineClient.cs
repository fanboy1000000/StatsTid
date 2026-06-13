namespace StatsTid.Backend.Api.Http;

/// <summary>
/// S73 / TASK-7300 (SPRINT-73 R1) — the NAMED backend→rule-engine HttpClient.
///
/// <para>
/// ONE mechanism for rule-engine auth carriage: every Backend call family that crosses the
/// rule-engine HTTP seam (validate-entitlement ×2 in SkemaEndpoints, check-compliance in
/// ComplianceEndpoints, check-overtime-governance in OvertimeEndpoints) resolves
/// <c>IHttpClientFactory.CreateClient(RuleEngineClient.Name)</c>. The named registration in
/// <c>Program.cs</c> wires the <see cref="RuleEngineHeaderForwardingHandler"/> (Authorization +
/// X-Correlation-Id forwarding) and the BaseAddress from <see cref="BaseUrlConfigKey"/>, so call
/// sites use RELATIVE request URIs and never re-implement header forwarding ad hoc — two
/// coexisting mechanisms are exactly the wiring drift that caused the S73 incident (bare
/// clients carrying no bearer → rule engine 401 → blanket 503 in the composed stack).
/// </para>
///
/// <para>
/// R1a scoping: the forwarding handler attaches ONLY to this named client — never the default
/// client builder. A non-rule-engine outbound client must NOT carry the user's bearer to a
/// foreign host (pinned negatively in <c>RuleEngineAuthForwardingTests</c>).
/// </para>
/// </summary>
public static class RuleEngineClient
{
    /// <summary>The named-client key for <c>IHttpClientFactory.CreateClient</c>.</summary>
    public const string Name = "RuleEngine";

    /// <summary>The existing config key the bare call sites read before S73 — unchanged.</summary>
    public const string BaseUrlConfigKey = "ServiceUrls:RuleEngine";

    /// <summary>The pre-S73 inline fallback, preserved verbatim (compose service DNS name).</summary>
    public const string DefaultBaseUrl = "http://rule-engine:8080";
}
