using Microsoft.Extensions.Logging;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Merges central AgreementRuleConfig with local DB overrides per ADR-010.
/// The rule engine NEVER loads local configs — this service provides the merge
/// at the service layer. Central config is authoritative; local configs can only
/// adjust within central constraints.
/// </summary>
public sealed class ConfigResolutionService
{
    private readonly AgreementConfigRepository _agreementConfigRepo;
    private readonly LocalConfigurationRepository _localConfigRepo;
    private readonly PositionOverrideRepository _positionOverrideRepo;
    private readonly ILogger<ConfigResolutionService> _logger;

    /// <summary>
    /// Config keys that are centrally negotiated and MUST NOT be overridden locally.
    /// These include overtime/merarbejde flags, supplement rates and toggles, and on-call settings.
    /// </summary>
    private static readonly HashSet<string> ProtectedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "HasOvertime",
        "HasMerarbejde",
        "EveningSupplementEnabled",
        "NightSupplementEnabled",
        "WeekendSupplementEnabled",
        "HolidaySupplementEnabled",
        "EveningRate",
        "NightRate",
        "WeekendSaturdayRate",
        "WeekendSundayRate",
        "HolidayRate",
        "EveningStart",
        "EveningEnd",
        "NightStart",
        "NightEnd",
        "OvertimeThreshold50",
        "OvertimeThreshold100",
        "OnCallDutyEnabled",
        "OnCallDutyRate",
    };

    public ConfigResolutionService(
        AgreementConfigRepository agreementConfigRepo,
        LocalConfigurationRepository localConfigRepo,
        PositionOverrideRepository positionOverrideRepo,
        ILogger<ConfigResolutionService> logger)
    {
        _agreementConfigRepo = agreementConfigRepo;
        _localConfigRepo = localConfigRepo;
        _positionOverrideRepo = positionOverrideRepo;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the effective config for an org by merging central config with local overrides.
    /// Central config is the base; local overrides are applied on top within constraints.
    /// The result is a plain AgreementRuleConfig that the rule engine receives as-is.
    /// </summary>
    public async Task<AgreementRuleConfig> ResolveAsync(
        string orgId, string agreementCode, string okVersion, string? position = null, CancellationToken ct = default)
    {
        // Get base config from DB (ADR-014), then apply position override if applicable.
        // Resolution chain: DB (ACTIVE) → Position Override → Local Override
        AgreementRuleConfig centralConfig;
        try
        {
            var dbConfig = await _agreementConfigRepo.GetActiveAsync(agreementCode, okVersion, ct);
            if (dbConfig is not null)
            {
                centralConfig = dbConfig.ToRuleConfig();
            }
            else
            {
                // Emergency fallback to static configs (defense in depth)
                _logger.LogWarning(
                    "No ACTIVE DB config found for {AgreementCode}/{OkVersion} — falling back to static CentralAgreementConfigs",
                    agreementCode, okVersion);
                centralConfig = CentralAgreementConfigs.TryGetConfig(agreementCode, okVersion)
                    ?? throw new InvalidOperationException(
                        $"No agreement configuration found for {agreementCode}/{okVersion}");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogWarning(ex,
                "Failed to load DB config for {AgreementCode}/{OkVersion} — falling back to static CentralAgreementConfigs",
                agreementCode, okVersion);
            centralConfig = CentralAgreementConfigs.TryGetConfig(agreementCode, okVersion)
                ?? throw new InvalidOperationException(
                    $"No agreement configuration found for {agreementCode}/{okVersion}");
        }

        // Apply position override on top of central config (before local overrides)
        if (position is not null)
        {
            PositionOverrideConfigs.PositionConfigOverride? positionOverride = null;
            try
            {
                var dbOverride = await _positionOverrideRepo.GetActiveAsync(agreementCode, okVersion, position, ct);
                if (dbOverride is not null)
                {
                    positionOverride = dbOverride.ToPositionConfigOverride();
                }
                else
                {
                    // Fallback to static config if no DB override found
                    positionOverride = PositionOverrideConfigs.TryGetOverride(agreementCode, okVersion, position);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to load DB position override for {AgreementCode}/{OkVersion}/{Position} — falling back to static",
                    agreementCode, okVersion, position);
                positionOverride = PositionOverrideConfigs.TryGetOverride(agreementCode, okVersion, position);
            }

            if (positionOverride is not null)
            {
                centralConfig = PositionOverrideConfigs.ApplyOverride(centralConfig, positionOverride);
            }
        }

        IReadOnlyList<LocalConfiguration> localOverrides;
        try
        {
            localOverrides = await _localConfigRepo.GetActiveByOrgAsync(orgId, agreementCode, okVersion, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load local overrides for org {OrgId}/{AgreementCode}/{OkVersion} — returning central config",
                orgId, agreementCode, okVersion);
            return centralConfig;
        }

        if (localOverrides.Count == 0)
        {
            _logger.LogDebug(
                "No local overrides for org {OrgId}/{AgreementCode}/{OkVersion} — returning central config",
                orgId, agreementCode, okVersion);
            return centralConfig;
        }

        // Start with central values, apply valid local overrides
        var mergedWeeklyNormHours = centralConfig.WeeklyNormHours;
        var mergedMaxFlexBalance = centralConfig.MaxFlexBalance;
        var mergedFlexCarryoverMax = centralConfig.FlexCarryoverMax;

        foreach (var local in localOverrides)
        {
            if (ProtectedKeys.Contains(local.ConfigKey))
            {
                _logger.LogWarning(
                    "Local override for protected key '{ConfigKey}' in org {OrgId} rejected — centrally negotiated values cannot be overridden",
                    local.ConfigKey, orgId);
                continue;
            }

            switch (local.ConfigKey)
            {
                case "MaxFlexBalance":
                    if (TryParseDecimal(local.ConfigValue, out var maxFlex)
                        && maxFlex > 0 && maxFlex <= centralConfig.MaxFlexBalance)
                    {
                        mergedMaxFlexBalance = maxFlex;
                        _logger.LogInformation(
                            "Local override applied: MaxFlexBalance = {Value} for org {OrgId}",
                            maxFlex, orgId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Invalid local override MaxFlexBalance = '{Value}' for org {OrgId} — must be > 0 and <= {Max}. Skipping.",
                            local.ConfigValue, orgId, centralConfig.MaxFlexBalance);
                    }
                    break;

                case "FlexCarryoverMax":
                    if (TryParseDecimal(local.ConfigValue, out var carryover)
                        && carryover > 0 && carryover <= centralConfig.FlexCarryoverMax)
                    {
                        mergedFlexCarryoverMax = carryover;
                        _logger.LogInformation(
                            "Local override applied: FlexCarryoverMax = {Value} for org {OrgId}",
                            carryover, orgId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Invalid local override FlexCarryoverMax = '{Value}' for org {OrgId} — must be > 0 and <= {Max}. Skipping.",
                            local.ConfigValue, orgId, centralConfig.FlexCarryoverMax);
                    }
                    break;

                case "WeeklyNormHours":
                    if (TryParseDecimal(local.ConfigValue, out var normHours)
                        && normHours > 0 && normHours <= 40)
                    {
                        mergedWeeklyNormHours = normHours;
                        _logger.LogInformation(
                            "Local override applied: WeeklyNormHours = {Value} for org {OrgId}",
                            normHours, orgId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Invalid local override WeeklyNormHours = '{Value}' for org {OrgId} — must be > 0 and <= 40. Skipping.",
                            local.ConfigValue, orgId);
                    }
                    break;

                case "PlanningStartDay":
                case "ApprovalCutoffDay":
                    // Informational only — does not affect AgreementRuleConfig
                    _logger.LogDebug(
                        "Informational local config '{ConfigKey}' = '{ConfigValue}' for org {OrgId} — not applied to AgreementRuleConfig",
                        local.ConfigKey, local.ConfigValue, orgId);
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown local config key '{ConfigKey}' for org {OrgId} — skipping",
                        local.ConfigKey, orgId);
                    break;
            }
        }

        // Construct merged config preserving all central values except the overridden ones
        return new AgreementRuleConfig
        {
            AgreementCode = centralConfig.AgreementCode,
            OkVersion = centralConfig.OkVersion,
            WeeklyNormHours = mergedWeeklyNormHours,
            HasOvertime = centralConfig.HasOvertime,
            HasMerarbejde = centralConfig.HasMerarbejde,
            MaxFlexBalance = mergedMaxFlexBalance,
            FlexCarryoverMax = mergedFlexCarryoverMax,
            EveningSupplementEnabled = centralConfig.EveningSupplementEnabled,
            NightSupplementEnabled = centralConfig.NightSupplementEnabled,
            WeekendSupplementEnabled = centralConfig.WeekendSupplementEnabled,
            HolidaySupplementEnabled = centralConfig.HolidaySupplementEnabled,
            EveningStart = centralConfig.EveningStart,
            EveningEnd = centralConfig.EveningEnd,
            NightStart = centralConfig.NightStart,
            NightEnd = centralConfig.NightEnd,
            EveningRate = centralConfig.EveningRate,
            NightRate = centralConfig.NightRate,
            WeekendSaturdayRate = centralConfig.WeekendSaturdayRate,
            WeekendSundayRate = centralConfig.WeekendSundayRate,
            HolidayRate = centralConfig.HolidayRate,
            OvertimeThreshold50 = centralConfig.OvertimeThreshold50,
            OvertimeThreshold100 = centralConfig.OvertimeThreshold100,
            OnCallDutyEnabled = centralConfig.OnCallDutyEnabled,
            OnCallDutyRate = centralConfig.OnCallDutyRate,
            CallInWorkEnabled = centralConfig.CallInWorkEnabled,
            CallInMinimumHours = centralConfig.CallInMinimumHours,
            CallInRate = centralConfig.CallInRate,
            TravelTimeEnabled = centralConfig.TravelTimeEnabled,
            WorkingTravelRate = centralConfig.WorkingTravelRate,
            NonWorkingTravelRate = centralConfig.NonWorkingTravelRate,
            NormPeriodWeeks = centralConfig.NormPeriodWeeks,
            NormModel = centralConfig.NormModel,
            AnnualNormHours = centralConfig.AnnualNormHours,
        };
    }

    /// <summary>
    /// Validates a proposed local config value against central constraints.
    /// Returns (true, null) if valid, or (false, errorMessage) if the value violates constraints.
    /// </summary>
    public (bool Valid, string? Error) ValidateLocalOverride(
        string configKey, string configValue, AgreementRuleConfig centralConfig)
    {
        if (ProtectedKeys.Contains(configKey))
        {
            return (false, $"Config key '{configKey}' is centrally negotiated and cannot be overridden locally.");
        }

        switch (configKey)
        {
            case "MaxFlexBalance":
                if (!TryParseDecimal(configValue, out var maxFlex))
                    return (false, $"MaxFlexBalance value '{configValue}' is not a valid decimal.");
                if (maxFlex <= 0)
                    return (false, "MaxFlexBalance must be greater than 0.");
                if (maxFlex > centralConfig.MaxFlexBalance)
                    return (false, $"MaxFlexBalance {maxFlex} exceeds central maximum of {centralConfig.MaxFlexBalance}.");
                return (true, null);

            case "FlexCarryoverMax":
                if (!TryParseDecimal(configValue, out var carryover))
                    return (false, $"FlexCarryoverMax value '{configValue}' is not a valid decimal.");
                if (carryover <= 0)
                    return (false, "FlexCarryoverMax must be greater than 0.");
                if (carryover > centralConfig.FlexCarryoverMax)
                    return (false, $"FlexCarryoverMax {carryover} exceeds central maximum of {centralConfig.FlexCarryoverMax}.");
                return (true, null);

            case "WeeklyNormHours":
                if (!TryParseDecimal(configValue, out var normHours))
                    return (false, $"WeeklyNormHours value '{configValue}' is not a valid decimal.");
                if (normHours <= 0)
                    return (false, "WeeklyNormHours must be greater than 0.");
                if (normHours > 40)
                    return (false, $"WeeklyNormHours {normHours} exceeds maximum of 40.");
                return (true, null);

            case "PlanningStartDay":
            case "ApprovalCutoffDay":
                // Informational keys — no constraint
                return (true, null);

            default:
                // Unknown keys are allowed (informational)
                return (true, null);
        }
    }

    /// <summary>
    /// Returns the active config for a given agreement/version from DB, or null if not found.
    /// Falls back to static CentralAgreementConfigs if DB unavailable.
    /// </summary>
    public async Task<AgreementRuleConfig?> GetActiveConfigAsync(
        string agreementCode, string okVersion, CancellationToken ct = default)
    {
        try
        {
            var dbConfig = await _agreementConfigRepo.GetActiveAsync(agreementCode, okVersion, ct);
            if (dbConfig is not null)
                return dbConfig.ToRuleConfig();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load DB config for {AgreementCode}/{OkVersion} — falling back to static",
                agreementCode, okVersion);
        }

        return CentralAgreementConfigs.TryGetConfig(agreementCode, okVersion);
    }

    /// <summary>
    /// Returns the central config for a given agreement/version from static data.
    /// Retained as emergency fallback. Prefer GetActiveConfigAsync for normal use.
    /// </summary>
    public static AgreementRuleConfig? GetCentralConfig(string agreementCode, string okVersion)
    {
        return CentralAgreementConfigs.TryGetConfig(agreementCode, okVersion);
    }

    /// <summary>
    /// Returns whether a central config exists for the given agreement/version.
    /// </summary>
    public static bool HasCentralConfig(string agreementCode, string okVersion)
    {
        return CentralAgreementConfigs.HasConfig(agreementCode, okVersion);
    }

    /// <summary>
    /// Parses a JSON config value as a decimal. The value may be a raw number
    /// or a JSON-quoted string containing a number.
    /// </summary>
    private static bool TryParseDecimal(string jsonValue, out decimal result)
    {
        // Strip JSON quotes if present (config values are stored as JSON strings)
        var trimmed = jsonValue.Trim().Trim('"');
        return decimal.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, out result);
    }
}
