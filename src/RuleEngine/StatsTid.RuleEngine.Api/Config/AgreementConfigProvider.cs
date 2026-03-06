using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Config;

/// <summary>
/// Pure static function: returns agreement-specific rule configuration.
/// No I/O, no DB — delegates to CentralAgreementConfigs in SharedKernel.
/// </summary>
public static class AgreementConfigProvider
{
    public static AgreementRuleConfig GetConfig(string agreementCode, string okVersion)
    {
        return CentralAgreementConfigs.GetConfig(agreementCode, okVersion);
    }

    public static bool HasConfig(string agreementCode, string okVersion)
    {
        return CentralAgreementConfigs.HasConfig(agreementCode, okVersion);
    }

    public static IReadOnlyList<string> GetSupportedAgreements(string okVersion)
    {
        return CentralAgreementConfigs.GetSupportedAgreements(okVersion);
    }
}
