using System.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S68 / TASK-6805 (ADR-033 D3) — the deterministic, idempotent vacation period-close
/// BackgroundService. On the <see cref="DelegationExpiryService"/> 5-minute poll cadence it
/// detects every due <c>(employee, VACATION, closed-entitlement-year)</c> tuple — a closed ferieår
/// whose §21/§24 ferieafholdelsesperiode boundary (≈ 31 Dec of the year AFTER the ferieår closes)
/// has passed on the <b>Europe/Copenhagen business date</b> — and runs the atomic settlement pass
/// (<see cref="VacationSettlementService.SettleAsync"/>) once per due tuple, each in its own
/// transaction.
///
/// <para>
/// <b>Trigger-vs-quantity determinism (ADR-033 D3, priority 2/4).</b> The service decides only WHICH
/// tuples are due and WHEN to check — it carries NO settled quantity. The settled quantity is
/// <see cref="VacationSettlementService.SettleAsync"/>'s concern (a pure function of the immutable
/// snapshot it captures). The "is it time to check yet" poll trigger MAY read the wall clock
/// (acceptable per ADR-033 D3 — a trigger, not a replayed quantity); the per-tuple due boundary,
/// however, is derived from the injected <see cref="TimeProvider"/> converted to the
/// Europe/Copenhagen calendar (NEVER raw <c>CURRENT_DATE</c>; Codex Step-0b W4 + ADR-033 D3
/// boundary-timezone / follow-up (v)), so the close fires on the Danish business boundary, not a
/// UTC-midnight boundary that could be ±1 day off near year-end.
/// </para>
///
/// <para>
/// <b>Enumeration is employee/entitlement-config-driven, NOT balance-driven (Codex Step-0b W4).</b>
/// The due set is generated from the active-employee set × their resolved VACATION
/// <c>reset_month</c> geometry, so an employee with NO <c>entitlement_balances</c> row for a closed
/// year is still settled (a configless/empty year still settles to the same zero-state disposition
/// the S66 D9 row renders). The cheap anti-join against active <c>vacation_settlements</c> rows
/// (<c>SETTLED</c>/<c>PENDING_REVIEW</c>) pre-skips already-settled tuples; the in-lock re-check
/// inside <see cref="VacationSettlementService.SettleAsync"/> + the D5 partial-unique key are the
/// correctness backstop (a concurrent poller / missed-then-late poll still settles EXACTLY once).
/// </para>
///
/// <para>
/// <b>DI (DECLARED for the Orchestrator to wire in <c>Program.cs</c>):</b>
/// <code>
/// builder.Services.AddHostedService&lt;SettlementCloseService&gt;();
/// </code>
/// All of <see cref="SettlementCloseService"/>'s constructor dependencies —
/// <see cref="DbConnectionFactory"/>, <see cref="VacationSettlementService"/>,
/// <see cref="EntitlementConfigRepository"/>, <see cref="TimeProvider"/> (already registered as
/// <c>TimeProvider.System</c> at Program.cs:227) and the logger — are already in the container
/// (TASK-6804 registered <see cref="VacationSettlementService"/> + its repos). No new helper or
/// <see cref="TimeProvider"/> registration is required; only the <c>AddHostedService</c> line above.
/// </para>
/// </summary>
public sealed class SettlementCloseService : BackgroundService
{
    /// <summary>The entitlement type this slice settles (slice 1 = VACATION only; ADR-033 D13).</summary>
    private const string VacationType = "VACATION";

    /// <summary>YEAR_END trigger — the only trigger the slice-1a pass accepts (ADR-033 D5/D9).</summary>
    private const string YearEndTrigger = "YEAR_END";

    /// <summary>
    /// Poll cadence — mirrors <see cref="DelegationExpiryService"/> (ADR-033 D3 "the
    /// DelegationExpiryService 5-min-poll shape"). The boundary is a calendar boundary; a 5-minute
    /// poll detects a newly-crossed 31-Dec boundary within minutes and is idempotent thereafter.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Hard lower bound on the candidate entitlement-year scan when an employee has no
    /// <c>employment_start_date</c> (nullable; the S60/ADR-030 HR-corrected fact). 2020 predates the
    /// system's earliest modeled OK period (OK24 starts 2024-04-01;
    /// <see cref="StatsTid.SharedKernel.Calendar.OkVersionResolver"/>) and any greenfield ferieår, so
    /// it cannot miss a real closed year while keeping the <c>generate_series</c> band finite and
    /// small. Deterministic (not wall-clock-derived).
    /// </summary>
    private const int CandidateYearFloor = 2020;

    private readonly DbConnectionFactory _connectionFactory;
    private readonly VacationSettlementService _settlementService;
    private readonly EntitlementConfigRepository _configRepo;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SettlementCloseService> _logger;

    public SettlementCloseService(
        DbConnectionFactory connectionFactory,
        VacationSettlementService settlementService,
        EntitlementConfigRepository configRepo,
        TimeProvider timeProvider,
        ILogger<SettlementCloseService> logger)
    {
        _connectionFactory = connectionFactory;
        _settlementService = settlementService;
        _configRepo = configRepo;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseDueSettlementsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A whole-poll failure must not crash the loop (mirrors DelegationExpiryService) —
                // a missed poll still settles exactly once on a later pass (ADR-033 D3).
                _logger.LogError(ex, "SettlementCloseService: error during vacation settlement poll");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task CloseDueSettlementsAsync(CancellationToken ct)
    {
        // The Copenhagen BUSINESS date — the §21/§24 boundary comparison authority (ADR-033 D3 /
        // follow-up (v)). Derived from the injected TimeProvider's UTC instant converted to the
        // Europe/Copenhagen zone, NEVER raw CURRENT_DATE: near a 31-Dec boundary the UTC date and the
        // Danish date differ for ~1h each night, and a UTC-midnight comparison would settle a tuple a
        // day early/late. (The poll being DUE-to-run at all may read the wall clock — that is a
        // trigger, not the boundary; the boundary uses this controlled business date.)
        var copenhagenToday = CopenhagenToday();

        var dueTuples = await EnumerateDueTuplesAsync(copenhagenToday, ct);
        if (dueTuples.Count == 0) return;

        _logger.LogInformation(
            "SettlementCloseService: {Count} due VACATION settlement tuple(s) at Copenhagen date {Date}",
            dueTuples.Count, copenhagenToday);

        foreach (var (employeeId, entitlementYear) in dueTuples)
        {
            // One transaction per tuple (ADR-018 D3 atomic settlement). SettleAsync takes the
            // ADR-032 D4 employee advisory lock FIRST + does its in-lock single-settle re-check, so
            // concurrent pollers and a late re-poll are safe: exactly one settlement commits. We open
            // the tx; SettleAsync neither commits nor rolls back (its all-or-nothing contract spans
            // the row + carryover + events + audit), so the commit/rollback is ours.
            await using var conn = _connectionFactory.Create();
            await conn.OpenAsync(ct);
            // ReadCommitted: the advisory-lock wait + the in-lock re-check inside SettleAsync need a
            // fresh post-lock snapshot to observe a concurrent poller's just-committed settlement
            // row (RepeatableRead would pin the pre-lock snapshot and defeat the re-check — the same
            // reasoning OutboxPublisher documents for its publisher tx).
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            try
            {
                var outcome = await _settlementService.SettleAsync(
                    employeeId, VacationType, entitlementYear, YearEndTrigger, conn, tx, ct);
                await tx.CommitAsync(ct);

                if (outcome.DidSettle)
                    _logger.LogInformation(
                        "SettlementCloseService: settled VACATION {Year} for {EmployeeId} ({State})",
                        entitlementYear, employeeId, outcome.Row.SettlementState);
                else
                    // The in-lock re-check / 23505 backstop found an active settlement (a concurrent
                    // poller won, or a TERMINATION already settled this year — ADR-033 D5). Benign.
                    _logger.LogDebug(
                        "SettlementCloseService: VACATION {Year} for {EmployeeId} already settled (no-op)",
                        entitlementYear, employeeId);
            }
            catch (DuplicateActiveSettlementException)
            {
                // The single-settle backstop fired and escaped (defensive — SettleAsync normally
                // swallows this into AlreadySettled). A concurrent poller committed first; exactly one
                // settlement stands. Roll back our empty tx, log, and continue the loop (do NOT crash).
                await SafeRollbackAsync(tx, ct);
                _logger.LogDebug(
                    "SettlementCloseService: VACATION {Year} for {EmployeeId} lost the settle race (benign duplicate); continuing",
                    entitlementYear, employeeId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-tuple failure is isolated: roll back, log, continue (mirrors
                // DelegationExpiryService). A missed tuple is re-detected on the next poll and
                // settles exactly once then (ADR-033 D3).
                await SafeRollbackAsync(tx, ct);
                _logger.LogWarning(ex,
                    "SettlementCloseService: failed to settle VACATION {Year} for {EmployeeId}; continuing",
                    entitlementYear, employeeId);
            }
        }
    }

    // ------------------------------------------------------------------
    // Due-tuple enumeration (ADR-033 D3 / Codex Step-0b W4). Employee/entitlement-config-driven —
    // NOT balance-driven — so a closed year with no entitlement_balances row is still settled.
    // ------------------------------------------------------------------

    /// <summary>
    /// Build the due <c>(employeeId, entitlementYear)</c> set: for every ACTIVE employee, generate the
    /// candidate closed VACATION entitlement-years from their <c>employment_start_date</c> year
    /// (floor <see cref="CandidateYearFloor"/> when null) up to <paramref name="copenhagenToday"/>'s
    /// year, anti-join the active (<c>SETTLED</c>/<c>PENDING_REVIEW</c>) settlement rows in SQL, then
    /// apply the EXACT Copenhagen §21/§24 boundary filter per row in C# using the employee's resolved
    /// VACATION <c>reset_month</c>. The SQL produces a finite candidate band; the C# filter is the
    /// authoritative due predicate (so the boundary math lives in one auditable place, not split into
    /// SQL date arithmetic across reset geometries).
    /// </summary>
    private async Task<IReadOnlyList<(string EmployeeId, int EntitlementYear)>> EnumerateDueTuplesAsync(
        DateOnly copenhagenToday, CancellationToken ct)
    {
        // Upper bound of the candidate band: the latest entitlement-year whose boundary could already
        // have passed is bounded above by copenhagenToday.Year (for reset_month==1 the boundary is
        // 31 Dec of the SAME year as E; for reset_month>1 it is a year later — both ≤ today's year as
        // an over-approximation). The precise per-row filter below discards the not-yet-due tail.
        var candidateUpperYear = copenhagenToday.Year;

        // Active employees paired with their candidate closed years, MINUS any year that already has an
        // active settlement (the pre-skip optimization; SettleAsync's in-lock re-check is the
        // correctness backstop). generate_series yields one row per (employee, candidate year). The
        // anti-join excludes (employee, VACATION, year) tuples with a non-REVERSED settlement.
        var rows = new List<(string EmployeeId, int EntitlementYear, string AgreementCode, string OkVersion)>();
        await using (var conn = _connectionFactory.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT u.user_id,
                       gs.yr AS entitlement_year,
                       u.agreement_code,
                       u.ok_version
                FROM users u
                CROSS JOIN LATERAL generate_series(
                    GREATEST(@floor, COALESCE(EXTRACT(YEAR FROM u.employment_start_date)::int, @floor)),
                    @upper
                ) AS gs(yr)
                WHERE u.is_active = TRUE
                  AND NOT EXISTS (
                        SELECT 1 FROM vacation_settlements vs
                        WHERE vs.employee_id = u.user_id
                          AND vs.entitlement_type = @type
                          AND vs.entitlement_year = gs.yr
                          AND vs.settlement_state <> 'REVERSED'
                  )
                ORDER BY u.user_id, gs.yr
                """, conn);
            cmd.Parameters.AddWithValue("floor", CandidateYearFloor);
            cmd.Parameters.AddWithValue("upper", candidateUpperYear);
            cmd.Parameters.AddWithValue("type", VacationType);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }

        if (rows.Count == 0) return [];

        // Per-row exact due filter on the Copenhagen boundary, using the employee's resolved VACATION
        // reset_month. reset_month is IMMUTABLE per (entitlement_type, ok_version) natural key
        // (ADR-021 Q1) and uniform (9) across the seeded VACATION configs, so resolution is cached on
        // (agreement_code, ok_version). A row whose VACATION type is genuinely unconfigured under the
        // employee's live agreement is skipped (no geometry to settle against — SettleAsync would be
        // the one to fail loud if such a tuple were ever forced; here it is simply not yet due/known).
        var resetMonthCache = new Dictionary<(string Agreement, string Ok), int?>();
        var due = new List<(string EmployeeId, int EntitlementYear)>();

        foreach (var (employeeId, entitlementYear, agreementCode, okVersion) in rows)
        {
            var key = (agreementCode, okVersion);
            if (!resetMonthCache.TryGetValue(key, out var resetMonth))
            {
                var live = await _configRepo.GetCurrentOpenAsync(VacationType, agreementCode, okVersion, ct);
                resetMonth = live?.ResetMonth;
                resetMonthCache[key] = resetMonth;
            }
            if (resetMonth is null) continue; // VACATION unconfigured for this agreement — no boundary.

            if (IsBoundaryPassed(entitlementYear, resetMonth.Value, copenhagenToday))
                due.Add((employeeId, entitlementYear));
        }

        return due;
    }

    /// <summary>
    /// The §21/§24 ferieafholdelsesperiode boundary test for VACATION entitlement-year
    /// <paramref name="entitlementYear"/> (E, the calendar year of the ferieår START — the
    /// <c>ResolveEntitlementYear</c> convention) under <paramref name="resetMonth"/>, evaluated on the
    /// Europe/Copenhagen business date <paramref name="copenhagenToday"/>. Returns <c>true</c> once the
    /// boundary has PASSED (the close is due).
    ///
    /// <para>
    /// The closed ferieår ends on <c>(reset_month/1/E).AddYears(1).AddDays(-1)</c>; the
    /// ferieafholdelsesperiode (taking window) extends to <b>31 Dec of the calendar year in which that
    /// ferieår-end falls</b> — the verified §21 "31 Dec" deadline (ADR-033 D8, line-18 §-spine). For
    /// VACATION (reset_month 9) that is 31 Dec of E+1 (ferieår Sep 1 E .. Aug 31 E+1 ⇒ boundary
    /// 31 Dec E+1), exactly "≈ 31 Dec of the year AFTER the ferieår closes". The tuple is due strictly
    /// AFTER that date (on/after 1 Jan of the following year).
    /// </para>
    /// </summary>
    private static bool IsBoundaryPassed(int entitlementYear, int resetMonth, DateOnly copenhagenToday)
    {
        var ferieaarStart = new DateOnly(entitlementYear, resetMonth, 1);
        var ferieaarEnd = ferieaarStart.AddYears(1).AddDays(-1);
        var boundary = new DateOnly(ferieaarEnd.Year, 12, 31); // §21 31-Dec deadline of the ferieår-end year.
        return copenhagenToday > boundary;
    }

    private static async Task SafeRollbackAsync(NpgsqlTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A rollback failure (e.g. the tx already aborted) is non-fatal — the connection is
            // disposed by the enclosing await-using regardless. Swallowed so the poll loop continues.
        }
    }

    // ------------------------------------------------------------------
    // Europe/Copenhagen business-date helper (ADR-033 D3 boundary-timezone / follow-up (v)).
    // Scoped to this file (no SharedKernel/shared-helper edit; the Orchestrator may later hoist this
    // into the (v) business-timezone helper). Cross-platform zone-id lookup: IANA "Europe/Copenhagen"
    // on Linux/macOS (and .NET 6+ on Windows via ICU), Windows "Romance Standard Time" as the fallback.
    // ------------------------------------------------------------------

    private static readonly TimeZoneInfo CopenhagenZone = ResolveCopenhagenZone();

    private DateOnly CopenhagenToday()
    {
        var utcNow = _timeProvider.GetUtcNow(); // the injected seam — overridable in tests/hosts.
        var copenhagenNow = TimeZoneInfo.ConvertTime(utcNow, CopenhagenZone);
        return DateOnly.FromDateTime(copenhagenNow.DateTime);
    }

    private static TimeZoneInfo ResolveCopenhagenZone()
    {
        // Prefer the IANA id (canonical; works on Linux CI + the .NET ICU-backed Windows runtime).
        // Fall back to the Windows registry id. If neither resolves (a stripped container with no tz
        // database AND no ICU), fall back to UTC — the boundary is then UTC-based (degraded but never a
        // crash); production/CI both carry one of the two id sets.
        foreach (var id in new[] { "Europe/Copenhagen", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }
}
