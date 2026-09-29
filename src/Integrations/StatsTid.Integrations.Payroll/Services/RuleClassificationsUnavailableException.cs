using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// Thrown by an <see cref="IRuleClassificationProvider"/> that resolves the rule classification
/// set at runtime (today <see cref="HttpRuleClassificationProvider"/>) when it could not obtain
/// that set — the Rule Engine was unreachable, answered with an error, or sent an unreadable body
/// (TASK-14412, S144 Step 7a cycle 2, ruling B1).
///
/// <para>
/// <strong>Why this exists, in plain language.</strong> Before the payroll service calculates or
/// exports a month it asks the Rule Engine which rules may not be split across a mid-month change
/// (for example an agreement-code change on the 15th). If any such rule exists, the month is
/// refused rather than exported wrong. The old behaviour answered an outage with "no rules" — an
/// empty list — and an empty list means "nothing to refuse", so every payroll route planned blind
/// and could export a split month with wrong lines. "I could not find out" and "there is nothing"
/// are different answers; this exception is how the provider says the first one. The HTTP routes
/// turn it into a 503 before anything is calculated, written or exported, and the caller retries
/// when the Rule Engine is back.
/// </para>
///
/// <para>
/// The message is fixed and carries no upstream status, body, employee id or date: the provider
/// logs the upstream detail itself, and the routes never echo it.
/// </para>
/// </summary>
public sealed class RuleClassificationsUnavailableException : InvalidOperationException
{
    /// <summary>The fixed message; deliberately free of upstream detail.</summary>
    public const string FixedMessage =
        "The rule classification set could not be obtained from the Rule Engine.";

    public RuleClassificationsUnavailableException()
        : base(FixedMessage)
    {
    }
}
