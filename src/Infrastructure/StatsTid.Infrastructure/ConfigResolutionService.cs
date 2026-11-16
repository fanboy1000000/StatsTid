using Microsoft.Extensions.Logging;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// Merges the central <see cref="AgreementRuleConfig"/> with the
/// position override and the active local agreement profile per
/// ADR-010 + ADR-017 D3. The rule engine NEVER loads local configs —
/// this service performs the merge at the service layer and the rule
/// engine receives a plain <see cref="AgreementRuleConfig"/>.
///
/// <para>Resolution chain (closed pre-commit, ADR-017 D3):</para>
/// <list type="number">
///   <item><description>Central agreement config (from <see cref="AgreementConfigRepository"/>,
///   with static <see cref="CentralAgreementConfigs"/> as emergency fallback).</description></item>
///   <item><description>Position override (when <c>position</c> is supplied) — applied via
///   <see cref="PositionOverrideConfigs.ApplyOverride"/>.</description></item>
///   <item><description>Local agreement profile (S21) — per-column overlay, NULL means
///   "inherit the post-position-override value".</description></item>
/// </list>
///
/// <para>S21: the legacy per-key <see cref="LocalConfigurationRepository"/> patch-bag
/// path has been retired in favor of the typed
/// <see cref="LocalAgreementProfileRepository"/>. The legacy repository is still
/// constructor-injected (read path only) for backward-compatibility but is no
/// longer consulted by <see cref="ResolveAsync"/>.</para>
/// </summary>
public sealed class ConfigResolutionService
{
    private readonly AgreementConfigRepository _agreementConfigRepo;
    private readonly LocalConfigurationRepository _localConfigRepo;
    private readonly PositionOverrideRepository _positionOverrideRepo;
    private readonly LocalAgreementProfileRepository? _localAgreementProfileRepo;
    private readonly ILogger<ConfigResolutionService> _logger;

    /// <summary>
    /// Backward-compatible 4-arg constructor (pre-S21 shape). The new
    /// <see cref="LocalAgreementProfileRepository"/> dependency is set to <c>null</c>,
    /// which collapses Step 3 of the resolution chain into a no-op (returns the
    /// post-position-override config). Used by unit tests that construct the service
    /// without DI; production wiring uses the 5-arg overload below.
    /// </summary>
    public ConfigResolutionService(
        AgreementConfigRepository agreementConfigRepo,
        LocalConfigurationRepository localConfigRepo,
        PositionOverrideRepository positionOverrideRepo,
        ILogger<ConfigResolutionService> logger)
        : this(agreementConfigRepo, localConfigRepo, positionOverrideRepo,
               localAgreementProfileRepo: null, logger)
    {
    }

    /// <summary>
    /// S21 production constructor. Accepts the new
    /// <see cref="LocalAgreementProfileRepository"/> alongside the existing
    /// dependencies. When <paramref name="localAgreementProfileRepo"/> is
    /// <c>null</c>, the resolver short-circuits Step 3 (no profile applied).
    /// </summary>
    public ConfigResolutionService(
        AgreementConfigRepository agreementConfigRepo,
        LocalConfigurationRepository localConfigRepo,
        PositionOverrideRepository positionOverrideRepo,
        LocalAgreementProfileRepository? localAgreementProfileRepo,
        ILogger<ConfigResolutionService> logger)
    {
        _agreementConfigRepo = agreementConfigRepo;
        _localConfigRepo = localConfigRepo;
        _positionOverrideRepo = positionOverrideRepo;
        _localAgreementProfileRepo = localAgreementProfileRepo;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the effective config for an org by walking the central → position
    /// override → local-profile chain (ADR-017 D3). The result is a plain
    /// <see cref="AgreementRuleConfig"/> the rule engine receives as-is.
    /// </summary>
    public async Task<AgreementRuleConfig> ResolveAsync(
        string orgId, string agreementCode, string okVersion, string? position = null, CancellationToken ct = default)
    {
        // Step 1 — central config (DB → static fallback).
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

        // Step 2 — position override (only when an employee position is supplied).
        if (position is not null)
        {
            PositionOverrideConfigs.PositionConfigOverride? positionOverride;
            try
            {
                var dbOverride = await _positionOverrideRepo.GetActiveAsync(agreementCode, okVersion, position, ct);
                positionOverride = dbOverride is not null
                    ? dbOverride.ToPositionConfigOverride()
                    : PositionOverrideConfigs.TryGetOverride(agreementCode, okVersion, position);
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

        // Step 3 — local agreement profile (S21, ADR-017). Per-column overlay; NULL
        // on a profile column means "inherit the post-position-override value".
        if (_localAgreementProfileRepo is null)
        {
            // No profile repository wired (legacy 4-arg constructor / unit-test path):
            // skip Step 3. Production callers receive the new 5-arg constructor with
            // a real repository.
            return centralConfig;
        }

        LocalAgreementProfile? profile;
        try
        {
            profile = await _localAgreementProfileRepo.GetCurrentOpenAsync(orgId, agreementCode, okVersion, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to load local agreement profile for {OrgId}/{AgreementCode}/{OkVersion} — returning post-position-override config",
                orgId, agreementCode, okVersion);
            return centralConfig;
        }

        if (profile is null)
        {
            _logger.LogDebug(
                "No active local agreement profile for {OrgId}/{AgreementCode}/{OkVersion} — returning post-position-override config",
                orgId, agreementCode, okVersion);
            return centralConfig;
        }

        return ApplyProfileOverlay(centralConfig, profile, orgId);
    }

    /// <summary>
    /// Applies the per-column overlay from <paramref name="profile"/> on top of
    /// <paramref name="baseConfig"/>. Each non-NULL column on the profile overrides
    /// the corresponding <see cref="AgreementRuleConfig"/> field; NULL columns are
    /// inherited from <paramref name="baseConfig"/>.
    ///
    /// <para>Mapping (single source of truth — schema column → AgreementRuleConfig field):</para>
    /// <list type="bullet">
    ///   <item><description><c>weekly_norm_hours</c> → <see cref="AgreementRuleConfig.WeeklyNormHours"/></description></item>
    ///   <item><description><c>max_flex_balance</c> → <see cref="AgreementRuleConfig.MaxFlexBalance"/></description></item>
    ///   <item><description><c>max_overtime_hours_per_period</c> → <see cref="AgreementRuleConfig.MaxOvertimeHoursPerPeriod"/></description></item>
    ///   <item><description><c>overtime_requires_pre_approval</c> → <see cref="AgreementRuleConfig.OvertimeRequiresPreApproval"/></description></item>
    /// </list>
    /// </summary>
    private AgreementRuleConfig ApplyProfileOverlay(
        AgreementRuleConfig baseConfig, LocalAgreementProfile profile, string orgId)
    {
        var weeklyNormHours = baseConfig.WeeklyNormHours;
        var maxFlexBalance = baseConfig.MaxFlexBalance;
        var maxOvertimeHoursPerPeriod = baseConfig.MaxOvertimeHoursPerPeriod;
        var overtimeRequiresPreApproval = baseConfig.OvertimeRequiresPreApproval;

        if (profile.WeeklyNormHours is { } wnh)
        {
            weeklyNormHours = wnh;
            _logger.LogInformation(
                "Local profile override applied: WeeklyNormHours = {Value} for org {OrgId} (profile {ProfileId})",
                wnh, orgId, profile.ProfileId);
        }
        if (profile.MaxFlexBalance is { } mfb)
        {
            maxFlexBalance = mfb;
            _logger.LogInformation(
                "Local profile override applied: MaxFlexBalance = {Value} for org {OrgId} (profile {ProfileId})",
                mfb, orgId, profile.ProfileId);
        }
        if (profile.MaxOvertimeHoursPerPeriod is { } mot)
        {
            maxOvertimeHoursPerPeriod = mot;
            _logger.LogInformation(
                "Local profile override applied: MaxOvertimeHoursPerPeriod = {Value} for org {OrgId} (profile {ProfileId})",
                mot, orgId, profile.ProfileId);
        }
        if (profile.OvertimeRequiresPreApproval is { } orpa)
        {
            overtimeRequiresPreApproval = orpa;
            _logger.LogInformation(
                "Local profile override applied: OvertimeRequiresPreApproval = {Value} for org {OrgId} (profile {ProfileId})",
                orpa, orgId, profile.ProfileId);
        }

        return new AgreementRuleConfig
        {
            AgreementCode = baseConfig.AgreementCode,
            OkVersion = baseConfig.OkVersion,
            WeeklyNormHours = weeklyNormHours,
            HasOvertime = baseConfig.HasOvertime,
            HasMerarbejde = baseConfig.HasMerarbejde,
            MaxFlexBalance = maxFlexBalance,
            EveningSupplementEnabled = baseConfig.EveningSupplementEnabled,
            NightSupplementEnabled = baseConfig.NightSupplementEnabled,
            WeekendSupplementEnabled = baseConfig.WeekendSupplementEnabled,
            HolidaySupplementEnabled = baseConfig.HolidaySupplementEnabled,
            EveningStart = baseConfig.EveningStart,
            EveningEnd = baseConfig.EveningEnd,
            NightStart = baseConfig.NightStart,
            NightEnd = baseConfig.NightEnd,
            EveningRate = baseConfig.EveningRate,
            NightRate = baseConfig.NightRate,
            WeekendSaturdayRate = baseConfig.WeekendSaturdayRate,
            WeekendSundayRate = baseConfig.WeekendSundayRate,
            HolidayRate = baseConfig.HolidayRate,
            OvertimeThreshold50 = baseConfig.OvertimeThreshold50,
            OvertimeThreshold100 = baseConfig.OvertimeThreshold100,
            OnCallDutyEnabled = baseConfig.OnCallDutyEnabled,
            OnCallDutyRate = baseConfig.OnCallDutyRate,
            CallInWorkEnabled = baseConfig.CallInWorkEnabled,
            CallInMinimumHours = baseConfig.CallInMinimumHours,
            CallInRate = baseConfig.CallInRate,
            TravelTimeEnabled = baseConfig.TravelTimeEnabled,
            WorkingTravelRate = baseConfig.WorkingTravelRate,
            NonWorkingTravelRate = baseConfig.NonWorkingTravelRate,
            NormPeriodWeeks = baseConfig.NormPeriodWeeks,
            NormModel = baseConfig.NormModel,
            AnnualNormHours = baseConfig.AnnualNormHours,
            MaxDailyHours = baseConfig.MaxDailyHours,
            MinimumRestHours = baseConfig.MinimumRestHours,
            RestPeriodDerogationAllowed = baseConfig.RestPeriodDerogationAllowed,
            WeeklyMaxHoursReferencePeriod = baseConfig.WeeklyMaxHoursReferencePeriod,
            VoluntaryUnsocialHoursAllowed = baseConfig.VoluntaryUnsocialHoursAllowed,
            DefaultCompensationModel = baseConfig.DefaultCompensationModel,
            EmployeeCompensationChoice = baseConfig.EmployeeCompensationChoice,
            MaxOvertimeHoursPerPeriod = maxOvertimeHoursPerPeriod,
            OvertimeRequiresPreApproval = overtimeRequiresPreApproval,
        };
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
}
