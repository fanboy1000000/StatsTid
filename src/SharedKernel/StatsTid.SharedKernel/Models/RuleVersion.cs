namespace StatsTid.SharedKernel.Models;

public sealed class RuleVersion
{
    public required string RuleId { get; init; }
    public required string OkVersion { get; init; }
    public required string RuleName { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public required string AgreementCode { get; init; }
}
