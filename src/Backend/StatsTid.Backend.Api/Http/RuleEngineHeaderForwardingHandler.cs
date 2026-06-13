namespace StatsTid.Backend.Api.Http;

/// <summary>
/// S73 / TASK-7300 (SPRINT-73 R1) — forwards the CURRENT inbound request's
/// <c>Authorization</c> and <c>X-Correlation-Id</c> headers onto outgoing rule-engine calls.
/// Registered ONLY on the named <see cref="RuleEngineClient.Name"/> client (R1a) — never the
/// default client builder, so non-rule-engine outbound clients never leak the user's bearer
/// to other hosts.
///
/// <para>
/// <b>The established auth-carriage partition (R1, documented in-code by mandate):</b>
/// <list type="bullet">
/// <item><description><b>FORWARD when an actor exists</b> — an ambient <see cref="HttpContext"/>
/// means a user's request is in flight; the user's own bearer crosses the hop so the rule
/// engine authorizes the SAME principal (the <c>WeeklyCalculationPipeline.cs:47-51</c>
/// Orchestrator precedent, which also carries X-Correlation-Id for P3 cross-hop traces).
/// </description></item>
/// <item><description><b>MINT when no HttpContext exists</b> — a background/scheduled caller has
/// no inbound request to forward; it must mint its own service token (the Payroll
/// <c>HttpRuleClassificationProvider</c> precedent, S20). This handler deliberately does NOT
/// mint: with no ambient HttpContext it attaches nothing, and the first Backend background
/// caller of the rule engine must follow the minting precedent rather than piggyback here.
/// </description></item>
/// </list>
/// </para>
///
/// <para>
/// Absent or empty inbound headers ⇒ NO outgoing header — never an empty value. A header
/// already set explicitly by the call site wins (no double-append).
/// </para>
/// </summary>
public sealed class RuleEngineHeaderForwardingHandler : DelegatingHandler
{
    private const string AuthorizationHeader = "Authorization";
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public RuleEngineHeaderForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // No ambient HttpContext (background caller) ⇒ forward nothing — see the MINT half
        // of the partition in the class doc.
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            CopyInboundHeader(httpContext, request, AuthorizationHeader);
            CopyInboundHeader(httpContext, request, CorrelationIdHeader);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static void CopyInboundHeader(
        HttpContext httpContext, HttpRequestMessage request, string headerName)
    {
        // An explicitly caller-set header wins — never double-append.
        if (request.Headers.Contains(headerName))
            return;

        if (!httpContext.Request.Headers.TryGetValue(headerName, out var values))
            return;

        var value = values.ToString();
        if (string.IsNullOrEmpty(value))
            return; // absent/empty inbound ⇒ NO outgoing header, never an empty value

        request.Headers.TryAddWithoutValidation(headerName, value);
    }
}
