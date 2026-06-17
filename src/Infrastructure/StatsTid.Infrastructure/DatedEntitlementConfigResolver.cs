using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S81 / TASK-8101 (R3) — the hoisted, GRACEFUL dated-entitlement-config resolution family,
/// extracted byte-for-byte from the YearOverview handler's former local functions
/// (<c>ResolveAgreementAtAsync</c> / <c>ResolveFallbackLiveAsync</c> / <c>ResolveDatedConfigAsync</c>,
/// BalanceEndpoints.cs S80-era :801-844). Behaviour-IDENTICAL: this is a pure mechanical lift, no
/// logic change.
///
/// <para>
/// <b>Why a per-request instance (not a singleton).</b> The resolver carries two REQUEST-SCOPED
/// caches — agreement-by-date and live-by-(type, agreement) — that the YearOverview matrix loop
/// (≤3 distinct ferieår starts × ≤4 categories) shares to bound repository reads. Those caches are
/// keyed to a single (employee, OkVersion, todayAgreementCode) request and must NOT leak across
/// requests, so the instance is minted per request by <see cref="DatedEntitlementConfigResolverFactory"/>
/// (the singleton, holding the two repositories) via <see cref="DatedEntitlementConfigResolverFactory.Create"/>.
/// </para>
///
/// <para>
/// <b>Graceful-only (ADR-023 D3, never 500).</b> This is the GRACEFUL family (S81 R4): the fail-CLOSED
/// settlement resolvers (<c>VacationSettlementService</c>) are deliberately NOT folded in. The dated
/// read misses fall through a live-config chain rather than throwing.
/// </para>
///
/// <para>
/// <b>Repository I/O.</b> Holds the entitlement-config repo + the user-agreement-code repo, so it
/// lives in Infrastructure (NOT the pure SharedKernel <see cref="EntitlementPeriodResolver"/>). Uses
/// the non-tx <see cref="EntitlementConfigRepository.GetByTypeAtAsync(string, string, string, DateOnly, System.Threading.CancellationToken)"/>
/// read overload (YearOverview is a pure read; the Skema consumer in TASK-8102 is a pre-transaction
/// validation, so the non-tx overload suffices there too).
/// </para>
/// </summary>
public sealed class DatedEntitlementConfigResolver
{
    private readonly EntitlementConfigRepository _entitlementConfigRepo;
    private readonly UserAgreementCodeRepository _userAgreementCodeRepo;

    // ── Per-request invariants (bound at Create-time) ──
    private readonly string _employeeId;
    private readonly string _liveOkVersion;       // user.OkVersion — the live-fallback OK operand.
    private readonly string _liveAgreementCode;   // user.AgreementCode — the live agreement fallback.
    private readonly string _todayAgreementCode;  // agreement dated at today (the "today's-agreement" branch operand).

    // ── Per-request caches (the load-bearing reason this is per-request, not a singleton) ──
    // A 12-month loop over ≤3 distinct ferieår starts must not issue 12 repo calls per category.
    private readonly Dictionary<DateOnly, string> _agreementByDate = new();
    // Null result is cached too (so a missing per-agreement live row is not re-queried per month).
    private readonly Dictionary<(string Type, string Agreement), EntitlementConfig?> _liveByTypeAgreement = new();

    internal DatedEntitlementConfigResolver(
        EntitlementConfigRepository entitlementConfigRepo,
        UserAgreementCodeRepository userAgreementCodeRepo,
        string employeeId,
        string liveOkVersion,
        string liveAgreementCode,
        string todayAgreementCode)
    {
        _entitlementConfigRepo = entitlementConfigRepo;
        _userAgreementCodeRepo = userAgreementCodeRepo;
        _employeeId = employeeId;
        _liveOkVersion = liveOkVersion;
        _liveAgreementCode = liveAgreementCode;
        _todayAgreementCode = todayAgreementCode;
    }

    /// <summary>
    /// S65 Step-7a — the AGREEMENT CODE operand of a dated entitlement-config read, resolved at
    /// <paramref name="asOf"/>. When an employee changes agreement (e.g. AC→HK) the earlier
    /// ferieår must be valued with the AC code, not today's. Mirrors the header dated read
    /// (user_agreement_codes, ADR-023 D3 graceful fallback to the live cache). For a single-agreement
    /// employee every date resolves to the SAME code today resolves to, so this is byte-identical to
    /// the prior todayAgreementCode reads. Cached per request.
    /// </summary>
    public async Task<string> ResolveAgreementAtAsync(DateOnly asOf, CancellationToken ct = default)
    {
        if (_agreementByDate.TryGetValue(asOf, out var cached))
            return cached;
        var resolved = await _userAgreementCodeRepo.GetByUserIdAtAsync(_employeeId, asOf, ct)
            ?? _liveAgreementCode;
        _agreementByDate[asOf] = resolved;
        return resolved;
    }

    /// <summary>
    /// Fallback live (open) config for a (type, agreement) pair OTHER than today's. Only consulted
    /// when the dated read misses AND the per-ferieår agreement differs from today's — falling back
    /// to today's-agreement liveConfig in that case would re-introduce the cross-agreement bug. Cached
    /// per (type, agreement) to bound reads; a null result is cached too (so a missing per-agreement
    /// live row is not re-queried per month). OK version stays the live <c>user.OkVersion</c> operand
    /// (byte-identical to the former local function).
    /// </summary>
    public async Task<EntitlementConfig?> ResolveFallbackLiveAsync(
        string type, string agreement, CancellationToken ct = default)
    {
        var key = (type, agreement);
        if (_liveByTypeAgreement.TryGetValue(key, out var cached))
            return cached;
        var resolved = await _entitlementConfigRepo.GetCurrentOpenAsync(
            type, agreement, _liveOkVersion, ct);
        _liveByTypeAgreement[key] = resolved;
        return resolved;
    }

    /// <summary>
    /// Dated entitlement-config read anchored at the per-ferieår agreement code. OK version stays the
    /// year-start-anchored value passed in (<paramref name="okVersion"/>). Graceful fallback chain
    /// (ADR-023 D3, never 500): dated row → if the per-ferieår agreement == today's, the already-fetched
    /// live row for this type (<paramref name="liveConfig"/>) → otherwise the live open row of the
    /// per-ferieår agreement → and only if THAT is null, <paramref name="liveConfig"/>.
    /// </summary>
    public async Task<EntitlementConfig> ResolveDatedConfigAsync(
        string type, DateOnly ferieaarStart, string okVersion, EntitlementConfig liveConfig,
        CancellationToken ct = default)
    {
        var agreement = await ResolveAgreementAtAsync(ferieaarStart, ct);
        var dated = await _entitlementConfigRepo.GetByTypeAtAsync(
            type, agreement, okVersion, ferieaarStart, ct);
        if (dated is not null)
            return dated;
        if (string.Equals(agreement, _todayAgreementCode, StringComparison.Ordinal))
            return liveConfig;
        return await ResolveFallbackLiveAsync(type, agreement, ct) ?? liveConfig;
    }
}

/// <summary>
/// S81 / TASK-8101 (R3) — the singleton factory for <see cref="DatedEntitlementConfigResolver"/>.
/// Holds the two repositories (singletons); <see cref="Create"/> mints a per-request resolver bound
/// to that request's (employeeId, liveOkVersion, liveAgreementCode, todayAgreementCode) so the
/// resolver's caches stay request-scoped. Registered in Program.cs as a singleton.
/// </summary>
public sealed class DatedEntitlementConfigResolverFactory
{
    private readonly EntitlementConfigRepository _entitlementConfigRepo;
    private readonly UserAgreementCodeRepository _userAgreementCodeRepo;

    public DatedEntitlementConfigResolverFactory(
        EntitlementConfigRepository entitlementConfigRepo,
        UserAgreementCodeRepository userAgreementCodeRepo)
    {
        _entitlementConfigRepo = entitlementConfigRepo;
        _userAgreementCodeRepo = userAgreementCodeRepo;
    }

    /// <summary>
    /// Mints a per-request <see cref="DatedEntitlementConfigResolver"/>.
    /// </summary>
    /// <param name="employeeId">The employee whose dated agreement-code history is resolved.</param>
    /// <param name="liveOkVersion">The live <c>user.OkVersion</c> — the live-fallback OK operand.</param>
    /// <param name="liveAgreementCode">The live <c>user.AgreementCode</c> — the agreement fallback when
    /// no dated row covers a date.</param>
    /// <param name="todayAgreementCode">The agreement dated at today — the "today's-agreement" branch
    /// operand in <see cref="DatedEntitlementConfigResolver.ResolveDatedConfigAsync"/>.</param>
    public DatedEntitlementConfigResolver Create(
        string employeeId,
        string liveOkVersion,
        string liveAgreementCode,
        string todayAgreementCode)
        => new(
            _entitlementConfigRepo,
            _userAgreementCodeRepo,
            employeeId,
            liveOkVersion,
            liveAgreementCode,
            todayAgreementCode);
}
