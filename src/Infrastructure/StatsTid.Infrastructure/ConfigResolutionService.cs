using System.Data.Common;
using Microsoft.Extensions.Logging;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// The agreement configuration in force on a day (ADR-014, ADR-017 D3, ADR-043 D12): the one dated,
/// fail-closed lookup every configuration read goes through, except the two month figures (Teamoversigt's
/// expected norm and the balance summary's month norm), which still read the ACTIVE row until TASK-14828 (the ruled
/// interim, B-9). Both hosts register it as the same singleton
/// as <see cref="ConfigResolutionService"/>, which implements it. Of the production callers only
/// <c>DailyNormCalculator</c> takes this interface (so a Unit fixture can fake the lookup); the Backend
/// handlers that read a configuration themselves inject the concrete class.
/// </summary>
public interface IAgreementConfigLookup
{
    /// <summary>
    /// The full chain central → position override → local profile, each layer the one in force on
    /// <paramref name="asOf"/>. <paramref name="orgId"/> is the organisation the employee belonged to on that
    /// day (their dated record, C-13), never the live primary organisation; the local layer is always read.
    /// A null <paramref name="position"/> means no override layer. The OK version is the caller's, never
    /// derived here: a caller asking about an employee's day passes <c>OkVersionResolver.ResolveVersion(asOf)</c>
    /// (ADR-040 D4); the organisation's effective-configuration screen passes the organisation's own.
    /// </summary>
    /// <exception cref="ConfigLookupUnavailableException">A read of any layer failed with a database error.</exception>
    /// <exception cref="NoConfigInForceException">No published central version is in force on the day.</exception>
    Task<AgreementRuleConfig> ResolveAsync(
        string orgId, string agreementCode, string okVersion, string? position, DateOnly asOf,
        CancellationToken ct = default);

    /// <summary>
    /// The central layer alone, in force on <paramref name="asOf"/>: the same read and the same refusals as
    /// the first layer of <see cref="ResolveAsync"/>, with no override and no profile. For members no
    /// override or profile column can change: today its only callers are the overtime routes, for the
    /// compensation model and choice. TASK-14828 will also use it for days with no employment record, which
    /// price the central layer only.
    /// </summary>
    /// <exception cref="ConfigLookupUnavailableException">The read failed with a database error.</exception>
    /// <exception cref="NoConfigInForceException">No published central version is in force on the day.</exception>
    Task<AgreementRuleConfig> ResolveCentralAsync(
        string agreementCode, string okVersion, DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// Resolves the agreement configuration in force on a day and merges its three layers at the service
/// layer (ADR-014, ADR-017 D3, ADR-043 D12); the rule engine never loads a configuration and receives a
/// plain <see cref="AgreementRuleConfig"/> (ADR-002).
///
/// <para>Each layer is the one in force on the day asked for — "in force" has one definition, shared with
/// the configuration writers' worklist reach (C1):</para>
/// <list type="number">
///   <item><description><b>Central</b> — <see cref="AgreementConfigRepository.GetInForceAsync"/>: the
///   published version with the latest applies-from date on or before the day (of two dated the same day,
///   the one published last, NR-P11). None in force → <see cref="NoConfigInForceException"/>.</description></item>
///   <item><description><b>Position override</b> (when a position is given) —
///   <see cref="PositionOverrideRepository.GetInForceAsync"/>: the key's newest version row on or before the
///   day across every override id (NR-P8); an ACTIVE version applies through
///   <see cref="PositionOverrideConfigs.ApplyOverride"/>, an INACTIVE one or none means no override
///   (A-9).</description></item>
///   <item><description><b>Local profile</b> — <see cref="LocalAgreementProfileRepository.GetInForceAsync"/>:
///   the organisation's row covering the day, end-exclusive (ADR-018 D8); a per-column overlay where NULL
///   inherits the post-override value. None → no local change.</description></item>
/// </list>
///
/// <para><b>Fail-closed (P-13; QUAL-184, QUAL-042).</b> No compiled value is ever substituted: a database
/// error on any layer is <see cref="ConfigLookupUnavailableException"/>, a day with no published central
/// version is <see cref="NoConfigInForceException"/>, and the lookup logs each refusal once. The tables
/// compiled into SharedKernel's configuration classes are never this lookup's answer (ADR-014; init.sql
/// seeds the database). The Backend answers both refusals through one mapping
/// (<c>ConfigLookupRefusalMiddleware</c>). Payroll registers the lookup but does not call it yet, so it
/// maps neither; TASK-14827 adds payroll's own mapping, a 503 and a 422 (wave-5 batch-C ruling 12).</para>
///
/// <para>No cache, no <c>(conn, tx)</c> overload and no lock: each read opens its own connection, so a caller
/// may call the lookup before or inside its own transaction without taking the pay-inputs lock (S148 PIL,
/// rule 3).</para>
///
/// <para>The <see cref="LocalConfigurationRepository"/> parameter is stored and never read; it stays
/// because a Unit fact constructs the service with it.</para>
/// </summary>
public sealed class ConfigResolutionService : IAgreementConfigLookup
{
    private readonly AgreementConfigRepository _agreementConfigRepo;
    private readonly LocalConfigurationRepository _localConfigRepo;
    private readonly PositionOverrideRepository _positionOverrideRepo;
    private readonly LocalAgreementProfileRepository _localAgreementProfileRepo;
    private readonly ILogger<ConfigResolutionService> _logger;

    /// <summary>
    /// The one constructor: every repository is required, so a host that lost a registration refuses to
    /// build under <c>ValidateOnBuild</c> instead of silently skipping a layer (QUAL-184).
    /// </summary>
    public ConfigResolutionService(
        AgreementConfigRepository agreementConfigRepo,
        LocalConfigurationRepository localConfigRepo,
        PositionOverrideRepository positionOverrideRepo,
        LocalAgreementProfileRepository localAgreementProfileRepo,
        ILogger<ConfigResolutionService> logger)
    {
        _agreementConfigRepo = agreementConfigRepo;
        _localConfigRepo = localConfigRepo;
        _positionOverrideRepo = positionOverrideRepo;
        _localAgreementProfileRepo = localAgreementProfileRepo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AgreementRuleConfig> ResolveAsync(
        string orgId, string agreementCode, string okVersion, string? position, DateOnly asOf,
        CancellationToken ct = default)
    {
        var request = new LookupRequest(agreementCode, okVersion, position, orgId, asOf);

        // Layer 1 — central, in force on the day.
        var config = await ReadCentralAsync(request, ct);

        // Layer 2 — the position override in force on the day (only when a position is supplied).
        if (position is not null)
        {
            var positionOverride = await ReadAsync(
                () => _positionOverrideRepo.GetInForceAsync(agreementCode, okVersion, position, asOf, ct),
                "position override", request);
            if (positionOverride is not null)
                config = PositionOverrideConfigs.ApplyOverride(config, positionOverride.ToPositionConfigOverride());
        }

        // Layer 3 — the organisation's local profile in force on the day.
        var profile = await ReadAsync(
            () => _localAgreementProfileRepo.GetInForceAsync(orgId, agreementCode, okVersion, asOf, ct),
            "local agreement profile", request);
        return profile is null ? config : ApplyProfileOverlay(config, profile, orgId);
    }

    /// <inheritdoc />
    public Task<AgreementRuleConfig> ResolveCentralAsync(
        string agreementCode, string okVersion, DateOnly asOf, CancellationToken ct = default) =>
        ReadCentralAsync(new LookupRequest(agreementCode, okVersion, Position: null, OrgId: null, asOf), ct);

    private async Task<AgreementRuleConfig> ReadCentralAsync(LookupRequest request, CancellationToken ct)
    {
        var central = await ReadAsync(
            () => _agreementConfigRepo.GetInForceAsync(request.AgreementCode, request.OkVersion, request.Day, ct),
            "central agreement configuration", request);
        if (central is not null)
            return central.ToRuleConfig();

        _logger.LogError(
            "No published agreement configuration is in force for {AgreementCode}/{OkVersion} on {Day} " +
            "(position {Position}, organisation {OrgId}); refused as config_not_in_force",
            request.AgreementCode, request.OkVersion, request.Day, request.Position ?? "(none)",
            request.OrgId ?? "(none)");
        throw new NoConfigInForceException(request.AgreementCode, request.OkVersion, request.Day);
    }

    /// <summary>
    /// Runs one layer's read. A database error (<see cref="DbException"/>, which <c>NpgsqlException</c> is)
    /// becomes <see cref="ConfigLookupUnavailableException"/> with the original as its inner exception;
    /// <see cref="OperationCanceledException"/> and every other exception (a defect, not an outage)
    /// propagate unchanged.
    /// </summary>
    private async Task<T?> ReadAsync<T>(Func<Task<T?>> read, string layer, LookupRequest request)
        where T : class
    {
        try
        {
            return await read();
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex,
                "The {Layer} read failed for {AgreementCode}/{OkVersion} on {Day} (position {Position}, " +
                "organisation {OrgId}); refused as unavailable",
                layer, request.AgreementCode, request.OkVersion, request.Day, request.Position ?? "(none)",
                request.OrgId ?? "(none)");
            throw new ConfigLookupUnavailableException(layer, ex);
        }
    }

    private readonly record struct LookupRequest(
        string AgreementCode, string OkVersion, string? Position, string? OrgId, DateOnly Day);

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
}

/// <summary>
/// A read of the agreement configuration failed with a database error (P-13): the lookup refuses rather
/// than substitute a compiled value. The original exception is <see cref="Exception.InnerException"/>. The
/// Backend answers it with a fixed 503 (<c>ConfigLookupRefusalMiddleware</c>). Payroll does not call the
/// lookup yet, so it maps nothing; TASK-14827 adds payroll's own 503 (wave-5 batch-C ruling 12).
/// </summary>
public sealed class ConfigLookupUnavailableException : Exception
{
    public ConfigLookupUnavailableException(string layer, Exception innerException)
        : base($"The {layer} read failed; the agreement configuration is unavailable.", innerException)
    {
        Layer = layer;
    }

    /// <summary>The layer whose read failed (server log only).</summary>
    public string Layer { get; }
}

/// <summary>
/// No published central version of (<see cref="AgreementCode"/>, <see cref="OkVersion"/>) is in force on
/// <see cref="Day"/> (P-13): refused by name, never answered with a compiled value. The members serve the
/// server log only; no response body carries them (ADR-040 D7). The Backend answers it with a fixed named
/// 500 (<c>config_not_in_force</c>, <c>ConfigLookupRefusalMiddleware</c>).
/// </summary>
public sealed class NoConfigInForceException : Exception
{
    public NoConfigInForceException(string agreementCode, string okVersion, DateOnly day)
        : base($"No published agreement configuration of {agreementCode}/{okVersion} is in force on {day:yyyy-MM-dd}.")
    {
        AgreementCode = agreementCode;
        OkVersion = okVersion;
        Day = day;
    }

    public string AgreementCode { get; }

    public string OkVersion { get; }

    public DateOnly Day { get; }
}
