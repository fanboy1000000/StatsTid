using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S68 / TASK-6804 (ADR-033 D3/D5/D6) — the atomic vacation-settlement pass. Exposes
/// <see cref="SettleAsync"/>, which the TASK-6805 <c>SettlementCloseService</c> BackgroundService
/// invokes once per due <c>(employee, VACATION, entitlement_year)</c> tuple inside a caller-owned
/// transaction.
///
/// <para>
/// <b>The pass (ALL in ONE caller-owned tx under the advisory lock; ADR-018 D3):</b>
/// <list type="number">
///   <item>acquire <c>pg_advisory_xact_lock(hashtext('employee-' || employeeId))</c> FIRST — the
///     SAME key as the ADR-032 D4 consumption lock, so a racing Skema-save / profile revaluation
///     writing <c>used</c> on the closing year's balance cannot interleave with this close writing
///     next-year <c>carryover_in</c>;</item>
///   <item>in-lock idempotency re-check — if an ACTIVE (non-REVERSED) settlement row already exists
///     for this tuple, no-op (a concurrent poller won the race; ADR-033 single-settle);</item>
///   <item>capture the IMMUTABLE snapshot (ADR-033 D3) — recorded per-absence feriedage, the
///     closed-year balance, the dated config, the §21 transfer-agreement days, impediment=false;</item>
///   <item>PARTITION the disposition — a PURE function of the snapshot (the legal core; matches the
///     S66 D9 <c>expiring</c> mapping);</item>
///   <item>WRITE the settlement row + audit, the §21 <c>carryover_in</c> on next year's balance,
///     emit the events on <c>employee-{id}</c> via the outbox, and the <c>audit_projection</c> row
///     sync-in-tx via <see cref="IAuditProjectionMapperRegistry"/> + <see cref="AuditProjectionRepository"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Quantity-determinism (ADR-033 D3, priority 2).</b> Every settled quantity is computed from
/// the captured snapshot — never re-derived after capture. The partition is a static pure function
/// (<see cref="Partition"/>); replay reads the recorded snapshot verbatim and reproduces the buckets
/// byte-identically.
/// </para>
///
/// <para>
/// <b>No <c>used</c> mutation / no balance "zeroing" (ADR-032 D2 + ADR-033 D6 clarification).</b>
/// Only §21 writes <c>carryover_in</c> (next year). The §24/§34 disposition lives on the settlement
/// row; <c>used</c> stays pinned to recorded absences and the closing ferieår's monthly <c>saldo</c>
/// is NOT retroactively zeroed (the balance readers special-case a settled year — TASK-6807).
/// </para>
/// </summary>
public sealed class VacationSettlementService
{
    private readonly EntitlementBalanceRepository _balanceRepo;
    private readonly EntitlementConfigRepository _configRepo;
    private readonly UserRepository _userRepo;
    private readonly UserAgreementCodeRepository _agreementCodeRepo;
    private readonly VacationTransferAgreementRepository _transferRepo;
    private readonly VacationSettlementRepository _settlementRepo;
    private readonly IEmploymentProfileResolver _profileResolver;
    private readonly IOutboxEnqueue _outbox;
    private readonly IAuditProjectionMapperRegistry _auditRegistry;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly ILogger<VacationSettlementService> _logger;

    private const string MonthlyAccrualModel = "MONTHLY_ACCRUAL";

    /// <summary>Snapshot JSON options — settle-time inputs persisted verbatim (camelCase, enum-as-string off).</summary>
    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public VacationSettlementService(
        EntitlementBalanceRepository balanceRepo,
        EntitlementConfigRepository configRepo,
        UserRepository userRepo,
        UserAgreementCodeRepository agreementCodeRepo,
        VacationTransferAgreementRepository transferRepo,
        VacationSettlementRepository settlementRepo,
        IEmploymentProfileResolver profileResolver,
        IOutboxEnqueue outbox,
        IAuditProjectionMapperRegistry auditRegistry,
        AuditProjectionRepository auditRepo,
        ILogger<VacationSettlementService> logger)
    {
        _balanceRepo = balanceRepo;
        _configRepo = configRepo;
        _userRepo = userRepo;
        _agreementCodeRepo = agreementCodeRepo;
        _transferRepo = transferRepo;
        _settlementRepo = settlementRepo;
        _profileResolver = profileResolver;
        _outbox = outbox;
        _auditRegistry = auditRegistry;
        _auditRepo = auditRepo;
        _logger = logger;
    }

    /// <summary>
    /// Settle one closed <c>(employee, entitlementType, entitlementYear)</c> in the caller-owned
    /// transaction. Idempotent: a no-op if an active settlement already exists (the in-lock
    /// re-check, plus the partial-unique-index 23505 backstop). The caller commits or rolls back —
    /// this method does NOT (so the read-your-write / forced-rollback all-or-nothing contract holds
    /// across the row + carryover + events + audit row).
    /// </summary>
    /// <param name="employeeId">The employee being settled.</param>
    /// <param name="entitlementType">The entitlement type (VACATION in slice 1).</param>
    /// <param name="entitlementYear">The entitlement year being closed (its ferieår-end is the boundary).</param>
    /// <param name="trigger">YEAR_END (poll) or TERMINATION.</param>
    /// <param name="conn">The caller's open connection (the advisory lock is taken on THIS connection).</param>
    /// <param name="tx">The caller's active transaction (all writes participate in it; ADR-018 D3).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The settlement outcome (its settled row, or <see cref="SettlementOutcome.AlreadySettled"/>).</returns>
    public async Task<SettlementOutcome> SettleAsync(
        string employeeId,
        string entitlementType,
        int entitlementYear,
        string trigger,
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CancellationToken ct = default)
    {
        // (0) BLOCKER 2 (Codex Step-5a) — slice 1a crystallizes YEAR_END ONLY. The whole pass below
        // (earned-to-BOUNDARY accrual, the §21/§24/§34 year-end partition, the §21 carryover to
        // entitlementYear+1) is year-end geometry. A TERMINATION (slice 3) settles to the
        // TERMINATION DATE, not the ferieår end — accepting it here would mis-credit accrual between
        // the termination date and the year boundary AND apply year-end partitioning. Fail loudly
        // until slice 3 adds the termination-date crystallization + partition.
        if (!string.Equals(trigger, "YEAR_END", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Vacation settlement trigger '{trigger}' is not supported in slice 1a (YEAR_END only). " +
                "TERMINATION settlement is slice 3 — not implemented (termination-date crystallization + partition).");
        }

        // (1) Advisory lock FIRST, on the caller's tx connection, held to commit (ADR-032 D4). Same
        // key as EmployeeConsumptionLock (hashtext('employee-' || id)) — serializes vs a racing
        // Skema-save / profile revaluation that writes `used` on the closing year's balance row.
        // (The helper lives in Backend.Api, out of this assembly's scope; the lock VALUE is what
        // matters for mutual exclusion, so the identical SQL is inlined here — DECLARED in the report.)
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx))
        {
            lockCmd.Parameters.AddWithValue("employeeId", employeeId);
            await lockCmd.ExecuteScalarAsync(ct);
        }

        // (2) In-lock idempotency re-check — if a concurrent poller already produced an active
        // (non-REVERSED) settlement for this tuple, no-op (ADR-033 single-settle).
        var existing = await _settlementRepo.GetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Vacation settlement no-op: active {State} settlement already exists for {EmployeeId}/{Type}/{Year} (in-lock re-check).",
                existing.SettlementState, employeeId, entitlementType, entitlementYear);
            return SettlementOutcome.AlreadySettled(existing);
        }

        // (3) Capture the immutable snapshot (ADR-033 D3). All inputs are pinned as-of the boundary;
        // quantities below are a pure function of this object (no live re-derivation after capture).
        var user = await _userRepo.GetByIdAsync(conn, tx, employeeId, ct)
            ?? throw new InvalidOperationException(
                $"Vacation settlement: employee {employeeId} not found or inactive.");

        var (snapshot, snapshotJson) = await CaptureSnapshotAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, user, ct);

        // (4) Partition the disposition — pure fn of the snapshot (the legal core; matches S66 D9).
        var partition = Partition(snapshot);

        // (5) Decide state. A §34 forfeiture-candidate (over_cap > 0) must NOT be auto-forfeited in
        // 1a (ADR-033 D10) — flag for human disposition. The §21/§24 buckets ARE executed in BOTH
        // states. (IsFeriehindret is false in slice 1; impediment modeling lands in slice 4.)
        var hasForfeitCandidate = partition.ForfeitDays > 0m;
        var settlementState = hasForfeitCandidate ? "PENDING_REVIEW" : "SETTLED";

        // (6) Atomic writes — settlement row + audit, §21 carryover, events (outbox), audit_projection.
        const int sequence = 1; // first settlement; reversal histories (slice 4) bump this.
        var row = new VacationSettlementRow
        {
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = entitlementYear,
            Sequence = sequence,
            SettlementState = settlementState,
            Trigger = trigger,
            SnapshotJson = snapshotJson,
            TransferDays = partition.TransferDays,
            PayoutDays = partition.PayoutDays,
            ForfeitDays = partition.ForfeitDays,
            Version = 1,
        };

        var actorId = $"system:settlement-close:{trigger}";
        const string actorRole = "System";

        VacationSettlementRow persisted;
        try
        {
            persisted = await _settlementRepo.InsertAsync(conn, tx, row, snapshotJson, actorId, actorRole, ct);
        }
        catch (DuplicateActiveSettlementException)
        {
            // The single-settle backstop fired between the in-lock re-check and the INSERT (a
            // concurrent poller committed first). Swallow benignly — exactly one settlement stands.
            _logger.LogInformation(
                "Vacation settlement no-op: 23505 single-settle backstop for {EmployeeId}/{Type}/{Year}.",
                employeeId, entitlementType, entitlementYear);
            var winner = await _settlementRepo.GetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
            return winner is not null
                ? SettlementOutcome.AlreadySettled(winner)
                : SettlementOutcome.AlreadySettled(row);
        }

        // §21 carryover (ADR-033 D5/D6) — DERIVED from transfer_days; idempotent (overwrite, not add).
        // In slice 1 the next-year carryover total IS the §21 term (the §22 term is 0). seedQuota is
        // next year's annual entitlement (the snapshot's dated annual_quota — VACATION quota is the
        // same across the boundary; the next-year row, if it materializes here, seeds at that quota).
        //
        // WARNING 1 (Codex Step-5a) — SKIP the carryover write when transfer is zero. WriteCarryoverInAsync
        // OVERWRITES next-year carryover_in (SET, not +=); calling it with 0 would CLOBBER any
        // carryover that an independent producer (a future §22 path, a manual seed) may already have
        // written to that next-year row. The §21 term is the ONLY carryover producer in slice 1, and
        // when there is no §21 transfer there is nothing for this pass to contribute — so leave the
        // next-year row untouched. (The >0 case keeps its derived-overwrite shape — re-run idempotent.)
        if (partition.TransferDays > 0m)
        {
            await _balanceRepo.WriteCarryoverInAsync(
                conn, tx, employeeId, entitlementType, entitlementYear + 1,
                carryoverDays: partition.TransferDays, seedQuota: snapshot.AnnualQuota, ct);
        }

        // Events on employee-{id} (ADR-018 D6) via the outbox + the ADR-026 audit_projection row
        // sync-in-tx per emitted event (ADR-026 D13; the BackgroundService dispatch site — a NEW
        // dispatch pattern that reuses the registry+repo seam, not an endpoint).
        var streamId = $"employee-{employeeId}";
        var auditTargetOrgId = user.PrimaryOrgId; // employee → primary_org_id (TENANT_TARGETED; ADR-033 D-mapping)

        if (partition.TransferDays > 0m)
        {
            var carryoverEvent = new VacationCarryoverExecuted
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = entitlementYear,
                Sequence = sequence,
                Snapshot = snapshot,
                TransferDays = partition.TransferDays,
                ActorId = actorId,
                ActorRole = actorRole,
            };
            await EmitAsync(conn, tx, streamId, carryoverEvent, actorId, auditTargetOrgId, ct);
        }

        if (partition.PayoutDays > 0m)
        {
            var payoutEvent = new VacationAutoPaidOut
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = entitlementYear,
                Sequence = sequence,
                Snapshot = snapshot,
                PayoutDays = partition.PayoutDays,
                ActorId = actorId,
                ActorRole = actorRole,
            };
            await EmitAsync(conn, tx, streamId, payoutEvent, actorId, auditTargetOrgId, ct);
        }

        if (hasForfeitCandidate)
        {
            var flaggedEvent = new SettlementManualReviewFlagged
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = entitlementYear,
                Sequence = sequence,
                Snapshot = snapshot,
                FlaggedDays = partition.ForfeitDays,
                ActorId = actorId,
                ActorRole = actorRole,
            };
            await EmitAsync(conn, tx, streamId, flaggedEvent, actorId, auditTargetOrgId, ct);
        }

        _logger.LogInformation(
            "Vacation settlement {State} for {EmployeeId}/{Type}/{Year}: transfer={Transfer} payout={Payout} forfeit={Forfeit}.",
            settlementState, employeeId, entitlementType, entitlementYear,
            partition.TransferDays, partition.PayoutDays, partition.ForfeitDays);

        return SettlementOutcome.Settled(persisted, partition);
    }

    // ------------------------------------------------------------------
    // Snapshot capture (ADR-033 D3). Reuses the EXACT D9 operands (BalanceEndpoints): the dated
    // config + earned-at-boundary via AccrualMath.EarnedToDate; the closed-year balance; the
    // recorded per-absence feriedage (ADR-032 D2). No re-valuation (ADR-033 D2).
    // ------------------------------------------------------------------

    private async Task<(VacationSettlementSnapshot Snapshot, string Json)> CaptureSnapshotAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, User user, CancellationToken ct)
    {
        // BLOCKER 1 (Codex Step-5a) — the dated entitlement-config resolution MUST reproduce the
        // EXACT chain the S66 D9 `expiring` figure uses (BalanceEndpoints.ResolveDatedConfigAsync),
        // so the settlement's annual_quota + carryover_max match D9 BYTE-FOR-BYTE. The prior code
        // keyed liveConfig on user.AgreementCode (the user's CURRENT live code) and used a bare
        // `?? liveConfig` fallback — so a missing dated row could settle on the WRONG (today's)
        // quota/cap. Below is the verbatim D9 chain:
        //
        //   todayAgreementCode = GetByUserIdAt(today) ?? user.AgreementCode
        //   liveConfig         = GetCurrentOpen(type, todayAgreementCode, user.OkVersion)   ← keyed on TODAY's code
        //                        // D9: may be NULL → bootstrapped from a historical-agreement probe
        //                        //     (`altLive`); NEVER throws. Reproduced below (the resetMonth
        //                        //     bootstrap, steps (a)/(b)/(c)) so a configless TODAY agreement
        //                        //     still settles any CLOSED year D9 renders.
        //   closedConfig       = ResolveDatedConfig(type, closedFerieaarStart, closedOk, liveConfig):
        //       agreement = GetByUserIdAt(closedFerieaarStart) ?? user.AgreementCode
        //       dated     = GetByTypeAt(type, agreement, closedOk, closedFerieaarStart)   → if non-null, RETURN
        //       else if agreement == todayAgreementCode → liveConfig
        //       else → GetCurrentOpen(type, agreement, user.OkVersion) ?? liveConfig
        //
        // NOTE (Step-5a P1/P4): the §24 wage-mapping KEY captured into the snapshot below uses the SAME
        // dated agreement but WITHOUT the `?? user.AgreementCode` fallback — it fails closed on a null
        // dated read (a missing dated row must not key a replay-sensitive payout off today's live code).
        // The VALUATION config-resolution above is unaffected (it only runs when the dated read is
        // non-null, so the fallback was already unreachable on that path).
        //
        // DECLARED `today` substitution: this service has no TimeProvider (the D9 reader takes one;
        // the not-yet-built TASK-6805 BackgroundService does not pass a settlement clock to this
        // pass). `todayAgreementCode` is a FALLBACK-branch / liveConfig operand ONLY — the dated
        // PRIMARY path (the legal core that fixes the closed year's quota/cap) is reproduced exactly
        // via GetByUserIdAt(closedFerieaarStart) + GetByTypeAt(…closedFerieaarStart). We substitute
        // todayAgreementCode := GetCurrentAsync (the live `effective_to IS NULL` user_agreement_codes
        // code), which EQUALS D9's GetByUserIdAt(today) for any non-future-dated live agreement —
        // always true when settling a CLOSED past year (no future-dated agreement can be live-as-of a
        // boundary already in the past). DECLARED in the report.
        var todayAgreementCode = await _agreementCodeRepo.GetCurrentAsync(employeeId, ct)
            ?? user.AgreementCode;

        // reset_month bootstrap (Codex cycle-2 BLOCKER fix) — discover ResetMonth so the boundary
        // can be computed BEFORE the dated read, WITHOUT throwing on a configless *today* agreement.
        //
        // The prior code keyed liveConfig on TODAY's code and THREW when it was null. But the S66 D9
        // reader (BalanceEndpoints.year-overview) NEVER throws: when today's agreement is configless
        // it bootstraps ResetMonth (and the ResolveDatedConfigAsync fallback terminal) from a probe
        // under the historical agreement (BalanceEndpoints ~line 755-792 `altLive`). A CLOSED VACATION
        // year that D9 renders via that bootstrap MUST be settleable — so the throw was wrong (it
        // refused a year D9 displays). reset_month is IMMUTABLE per (entitlement_type, ok_version)
        // natural key (ADR-021 Q1), so its VALUE is identical regardless of which agreement's config
        // supplies it; we may therefore read it from ANY resolvable config for the type, preferring
        // today's agreement (exact D9 parity when it IS configured).
        //
        // (a) today's-agreement live config — the D9 happy path (keyed on TODAY's code).
        var liveConfig = await _configRepo.GetCurrentOpenAsync(entitlementType, todayAgreementCode, user.OkVersion, ct);

        // (b) today configless → bootstrap from a YEAR-START anchor probe, replicating the S66 D9
        // reader EXACTLY (BalanceEndpoints ~lines 774-791, the `altLive` resolution). D9 probes a
        // candidate ferieår-START anchor SET — NOT a mid-ferieår date — skipping any anchor whose
        // agreement equals today's (which is already known configless), and takes the FIRST anchor
        // that yields a live config under its in-force agreement. That config seeds BOTH ResetMonth
        // discovery AND the ResolveDatedConfigAsync fallback terminal below.
        //
        // CYCLE-3 FIX (the defect): the prior single Dec-1 probe did NOT skip today's agreement, so a
        // mid-closed-ferieår switch to a CONFIGLESS agreement resolved the LATER (configless) code at
        // Dec-1 → no config → throw, even though D9 probes the YEAR-START (where the CONFIGURED prior
        // agreement was in force) and still renders that closed year. Mirroring D9's year-start anchor
        // set + `continue`-skip + first-hit precedence fixes the divergence: the service now resolves
        // (does not throw) for EVERY closed VACATION year D9 can render, and throws only where D9 would
        // render an empty row (no config under any agreement at any anchor).
        //
        // ANCHOR SET (D9 parity, expressed against the SETTLED entitlementYear): D9 works from a
        // CALENDAR view-year Y and probes {Jan-1 Y, Sep-1 Y−1, Sep-1 Y} — the three ferieår-starts that
        // can intersect calendar year Y under the two seeded reset geometries (1 and 9). This service is
        // handed the exact entitlementYear E; the ONLY ferieår-start that can BE E (and whose agreement
        // the dated PRIMARY read below uses) is Jan-1 E (reset-1 geometry) or Sep-1 E (reset-9 geometry)
        // — a strict SUBSET of D9's anchors for the year being settled (Jan-1 E is D9's first anchor when
        // viewing Y=E; Sep-1 E is D9's third when viewing Y=E and its second when viewing Y=E+1). So this
        // probe can never resolve a config D9 wouldn't. PRECEDENCE matches D9: calendar geometry (Jan-1)
        // first, then the Sep geometry (Sep-1), first hit wins (D9's documented best-effort tie-break).
        // ResetMonth is IMMUTABLE per (entitlement_type, ok_version) natural key (ADR-021 Q1), so its
        // VALUE is identical whichever agreement's config supplies it.
        if (liveConfig is null)
        {
            var probeAnchors = new[]
            {
                new DateOnly(entitlementYear, 1, 1), // calendar (reset-1) ferieår start for E
                new DateOnly(entitlementYear, 9, 1)  // Sep (reset-9) ferieår start for E
            };
            foreach (var anchor in probeAnchors)
            {
                // ResolveAgreementAtAsync parity: GetByUserIdAtAsync(anchor) ?? user.AgreementCode.
                var anchorAgreement = await _agreementCodeRepo.GetByUserIdAtAsync(employeeId, anchor, ct)
                    ?? user.AgreementCode;
                // D9's `continue`: skip today's code — already known configless (step (a) missed on it).
                if (string.Equals(anchorAgreement, todayAgreementCode, StringComparison.Ordinal))
                    continue;
                // ResolveFallbackLiveAsync parity: GetCurrentOpenAsync(type, anchorAgreement, user.OkVersion).
                var altLive = await _configRepo.GetCurrentOpenAsync(entitlementType, anchorAgreement, user.OkVersion, ct);
                if (altLive is not null)
                {
                    liveConfig = altLive; // ResetMonth discovery + ResolveDatedConfigAsync fallback terminal
                    break;                // first hit wins (D9 precedence)
                }
            }
        }

        // (c) No config resolvable for the type under ANY agreement at ANY anchor (today's, OR the
        // year-start agreements at Jan-1/Sep-1 of entitlementYear) — a genuinely unconfigured type;
        // a real error, throw. This is EXACTLY D9's terminal: it renders an empty row here (liveConfig
        // still null after the anchor probe). The service settles a real disposition, so it fails loud
        // rather than rendering empty — but the THROW CONDITION matches D9's empty-row condition
        // precisely (so the service settles every closed year D9 renders non-empty, and only this
        // genuinely-unconfigured case throws).
        if (liveConfig is null)
        {
            throw new InvalidOperationException(
                $"Vacation settlement: no entitlement config resolvable for {entitlementType} under " +
                $"today's agreement '{todayAgreementCode}' or the year-start agreements at Jan-1/Sep-1 " +
                $"{entitlementYear} (employee {employeeId}); the type appears unconfigured " +
                $"(ok_version {user.OkVersion}) — D9 would render an empty row here.");
        }
        var resetMonth = liveConfig.ResetMonth;

        // The closed ferieår [start, end] for the settled entitlementYear. reset_month==1 → calendar
        // year (Jan 1 .. Dec 31 of entitlementYear); else → the reset_month-based ferieår whose YEAR
        // is entitlementYear (e.g. VACATION reset_month 9 → Sep 1 (entitlementYear) .. Aug 31 (year+1)).
        DateOnly closedFerieaarStart;
        DateOnly boundaryDate;
        if (resetMonth == 1)
        {
            closedFerieaarStart = new DateOnly(entitlementYear, 1, 1);
            boundaryDate = new DateOnly(entitlementYear, 12, 31);
        }
        else
        {
            closedFerieaarStart = new DateOnly(entitlementYear, resetMonth, 1);
            boundaryDate = closedFerieaarStart.AddYears(1).AddDays(-1);
        }

        var okVersion = OkVersionResolver.ResolveVersion(closedFerieaarStart);

        // The STRICTLY-DATED historical agreement in force at the closed ferieår start. This serves TWO
        // distinct roles with DIFFERENT null semantics:
        //
        //  (a) the snapshot's §24 wage-mapping KEY (ADR-033 D7) — must be the strict dated value, NEVER a
        //      live fallback. A WARNING (Step-5a P1/P4) — the prior code put `?? user.AgreementCode` into
        //      the snapshot, so a missing dated row silently keyed the §24 payout off the employee's
        //      CURRENT live code, breaking replay determinism and risking a wrong-agreement lønart. Fail
        //      CLOSED here (symmetric with the Position fail-closed throw below): a settlement that cannot
        //      pin the dated agreement at the ferieår start must NOT stage a live/empty-keyed payout.
        //  (b) the D9-parity VALUATION's config-resolution agreement — which DELIBERATELY falls back to
        //      user.AgreementCode (the verbatim ResolveDatedConfigAsync chain). Computed below from the
        //      same strict value with the fallback re-applied, so the valuation path is byte-for-byte
        //      identical to today.
        var datedAgreementForSnapshot = await _agreementCodeRepo.GetByUserIdAtAsync(employeeId, closedFerieaarStart, ct)
            ?? throw new InvalidOperationException(
                $"Vacation settlement: no dated user_agreement_codes row covers {closedFerieaarStart:yyyy-MM-dd} " +
                $"(ferieår start) for employee {employeeId}; cannot capture the §24 wage-type-mapping " +
                "agreement_code (ADR-033 D7) — settlement capture fails closed rather than keying the payout " +
                "off the employee's current live agreement.");

        // ResolveDatedConfigAsync (verbatim D9): dated → today-code? liveConfig → fallback-live ?? liveConfig.
        // Byte-identical to the prior `GetByUserIdAtAsync(...) ?? user.AgreementCode`: the fail-closed throw
        // above means control only reaches here when the dated read was non-null, so the `?? user.AgreementCode`
        // fallback was already an unreachable branch — `agreementCode` takes the same value it took before.
        var agreementCode = datedAgreementForSnapshot;
        var dated = await _configRepo.GetByTypeAtAsync(
            entitlementType, agreementCode, okVersion, closedFerieaarStart, ct);
        EntitlementConfig datedConfig;
        if (dated is not null)
        {
            datedConfig = dated;
        }
        else if (string.Equals(agreementCode, todayAgreementCode, StringComparison.Ordinal))
        {
            datedConfig = liveConfig; // dated miss under today's agreement → the live (open) row
        }
        else
        {
            // dated miss under a DIFFERENT (historical) agreement → that agreement's live-open row,
            // else liveConfig (D9's ResolveFallbackLiveAsync terminal; keyed on user.OkVersion as D9).
            datedConfig = await _configRepo.GetCurrentOpenAsync(entitlementType, agreementCode, user.OkVersion, ct)
                ?? liveConfig;
        }

        // Closed-year balance — the EXACT D9 operands (ADR-032 D2: balance.Used IS the authoritative
        // recorded-feriedage sum written at booking; carryover_in entered the closed year; planned
        // is untaken-at-boundary). A missing balance row ⇒ zero-state (employee with no consumption).
        var closedBalance = await _balanceRepo.GetByEmployeeAndTypeAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);
        var closedUsed = closedBalance?.Used ?? 0m;
        var closedCarryoverIn = closedBalance?.CarryoverIn ?? 0m;
        var closedPlanned = closedBalance?.Planned ?? 0m;

        // earned-at-boundary (the D9 operand): MONTHLY accrues to the boundary; IMMEDIATE is the full
        // quota up-front. Fraction-independent day-count per S63/ADR-031 (1.0m).
        var earnedAtBoundary = string.Equals(datedConfig.AccrualModel, MonthlyAccrualModel, StringComparison.Ordinal)
            ? AccrualMath.EarnedToDate(datedConfig.AnnualQuota, 1.0m, closedFerieaarStart, user.EmploymentStartDate, boundaryDate)
            : datedConfig.AnnualQuota;

        // The recorded per-absence feriedage components (ADR-032 D2) within the closed ferieår — the
        // auditable breakdown carried in the snapshot. For VACATION the absence_type IS 'VACATION'
        // (EntitlementMapping). Authoritative "used" scalar stays closedBalance.Used (the D9 operand).
        var recordedAbsences = await ReadRecordedFeriedageAsync(
            conn, tx, employeeId, entitlementType, closedFerieaarStart, boundaryDate, ct);

        // §21 written transfer-agreement days (ADR-033 D8; 0 when no agreement — the law's §24 default).
        var transferAgreement = await _transferRepo.GetByKeyAsync(conn, tx, employeeId, entitlementYear, entitlementType, ct);
        var transferAgreementDays = transferAgreement?.TransferDays ?? 0m;

        // §24 wage-type-mapping natural key (ADR-033 D7 / ADR-020 (time_type, ok_version, agreement_code,
        // position)) captured into the immutable snapshot so the S69 §24 Payroll emitter resolves the
        // lønart off THIS snapshot (replay-deterministic, no live lookup). The `position` component is
        // read from the dated employee_profiles row (ADR-023) AS-OF closedFerieaarStart — the SAME
        // instant as agreementCode (line ~436) and okVersion (line ~433), so the four key components are
        // snapshot-internally consistent.
        //
        // FAIL-CLOSED (B3/B5): if no dated profile covers closedFerieaarStart, THROW — capture must fail
        // loudly rather than silently fall back to live/empty profile data and stage a wrong-keyed payout.
        // (A resolved profile whose Position is itself null/empty IS fine — pass it through; the emitter
        // canonicalizes null→"" for the wage_type_mappings.position '' default.)
        var settlementProfile = await _profileResolver.GetByEmployeeIdAtAsync(employeeId, closedFerieaarStart, ct)
            ?? throw new InvalidOperationException(
                $"Vacation settlement: no dated employee_profiles row covers {closedFerieaarStart:yyyy-MM-dd} " +
                $"(ferieår start) for employee {employeeId}; cannot capture the §24 wage-type-mapping " +
                "position (ADR-033 D7) — settlement capture fails closed rather than using live/empty data.");
        var settlementPosition = settlementProfile.Position;

        var snapshot = new VacationSettlementSnapshot
        {
            RecordedAbsences = recordedAbsences,
            Earned = earnedAtBoundary,
            Used = closedUsed,
            Planned = closedPlanned,
            CarryoverIn = closedCarryoverIn,
            AnnualQuota = datedConfig.AnnualQuota,
            CarryoverMax = datedConfig.CarryoverMax,
            ResetMonth = resetMonth,
            OkVersion = okVersion,
            // §24 wage-type natural key (ADR-033 D7 / ADR-020) — all pinned at closedFerieaarStart.
            AgreementCode = datedAgreementForSnapshot, // STRICTLY-dated ferieår-start agreement (fail-closed, no live fallback — Step-5a P1/P4)
            Position = settlementPosition,             // dated employee_profiles position (ADR-023)
            // FERIEÅR-END accrual boundary (Aug 31 for VACATION reset_month 9; Dec 31 only when reset_month==1) —
            // the inherited S68 valuation boundary computed above (~L427-441), reused as the §24 wage-mapping asOf.
            // FOLLOW-UP (S69 Step-7a W1): the LEGAL §24/§21 anchor is 31 Dec (Ferielov §21 stk.2; S65 research
            // docs/references/ferie-transfer-timing-research.md). Inert today (every §24 mapping is open-from-2020,
            // so Aug-31 vs 31-Dec asOf resolve identically); the owner must rule the asOf when the real §24 SLS
            // lønart lands and a dated supersession could fall between Aug 31 and 31 Dec. Value unchanged this sprint.
            SettlementBoundaryDate = boundaryDate,
            TransferAgreementDays = transferAgreementDays,
            IsFeriehindret = false, // slice 1 — §22 not modeled (ADR-033 D10)
        };

        var json = JsonSerializer.Serialize(snapshot, SnapshotJson);
        return (snapshot, json);
    }

    /// <summary>
    /// In-tx read of the recorded per-absence <c>feriedage</c> components (ADR-032 D2) for the
    /// settled entitlement type within the closed ferieår window. VACATION-only in slice 1 — the
    /// <c>absence_type</c> for VACATION entitlement is the literal <c>'VACATION'</c>
    /// (Backend.Api <c>EntitlementMapping</c>); rows with a NULL feriedage (un-backfilled legacy)
    /// are excluded from the components (the authoritative scalar is the balance's <c>used</c>).
    /// </summary>
    private static async Task<IReadOnlyList<RecordedAbsenceFeriedage>> ReadRecordedFeriedageAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, DateOnly start, DateOnly end, CancellationToken ct)
    {
        // entitlementType → absence_type. Slice 1 settles VACATION (absence_type 'VACATION'); for
        // forward-safety map the common types, defaulting to the identity (covers VACATION).
        var absenceType = entitlementType switch
        {
            "SPECIAL_HOLIDAY" => "SPECIAL_HOLIDAY_ALLOWANCE",
            _ => entitlementType, // VACATION/CARE_DAY/SENIOR_DAY are identity; CHILD_SICK has 3 variants (not settled in 1a)
        };

        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_id, feriedage
            FROM absences_projection
            WHERE employee_id = @employeeId
              AND absence_type = @absenceType
              AND date >= @start AND date <= @end
              AND feriedage IS NOT NULL
            ORDER BY outbox_id ASC
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("absenceType", absenceType);
        cmd.Parameters.AddWithValue("start", start);
        cmd.Parameters.AddWithValue("end", end);

        var list = new List<RecordedAbsenceFeriedage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(new RecordedAbsenceFeriedage(reader.GetGuid(0), reader.GetDecimal(1)));
        return list;
    }

    // ------------------------------------------------------------------
    // The legal partition (ADR-033 D5/D10). PURE function of the snapshot — the same operands as
    // the S66 D9 `expiring` figure (BalanceEndpoints): over_cap == that D9 figure exactly.
    // ------------------------------------------------------------------

    /// <summary>
    /// Partition the closed-year remainder into the §21/§24/§34 buckets. PURE function of the
    /// captured snapshot (ADR-033 D3 quantity-determinism; replay-stable). The arithmetic is done on
    /// exact decimals and each bucket is rounded to 2dp (the <c>NUMERIC(6,2)</c> storage precision)
    /// — <see cref="SettlementPartition.ForfeitDays"/> is computed identically to the S66 D9
    /// <c>expiring</c> figure (BalanceEndpoints): <c>round(max(0, raw − carryover_max), 2)</c>.
    /// </summary>
    /// <remarks>
    /// disposable = max(0, earnedAtBoundary + carryoverIn − used − planned) — this SUBTRACTS planned
    /// to byte-match the D9 <c>transferableRaw</c> (BalanceEndpoints). For a closed year planned is 0,
    /// so this coincides with the prompt's <c>disposable = max(0, earned + carryoverIn − used)</c>;
    /// keeping the planned term makes <c>over_cap</c> identical to D9's <c>expiring</c> regardless.
    /// </remarks>
    internal static SettlementPartition Partition(VacationSettlementSnapshot s)
    {
        var raw = s.Earned + s.CarryoverIn - s.Used - s.Planned;
        var disposable = Math.Max(0m, raw);

        var underCap = Math.Min(disposable, s.CarryoverMax);     // §21 + §24 tranche (≤ 5th-week ceiling)
        var overCap = Math.Max(0m, disposable - s.CarryoverMax); // §34 forfeiture-candidate (== D9 expiring)

        // WARNING 2 (Codex Step-5a) — clamp the agreement-days operand defensively. The DB CHECK
        // (transfer_days >= 0) now blocks STORING a negative agreement, but the partition stays robust
        // regardless of the snapshot source: transfer = max(0, min(agreementDays, underCap)) so a
        // stray negative can never produce a negative transfer/carryover nor inflate payout
        // (payoutDays = underCap − transferDays would otherwise EXCEED underCap on a negative transfer).
        var transferDays = Math.Max(0m, Math.Min(s.TransferAgreementDays, underCap)); // §21 (0 if no agreement)
        var payoutDays = underCap - transferDays;                                      // §24 (the law's default)
        var forfeitDays = overCap;                                                     // §34-candidate

        return new SettlementPartition(
            Disposable: Round2(disposable),
            UnderCap: Round2(underCap),
            OverCap: Round2(overCap),
            TransferDays: Round2(transferDays),
            PayoutDays: Round2(payoutDays),
            ForfeitDays: Round2(forfeitDays));
    }

    // WARNING 3 (Codex Step-5a) — the bucket rounding MUST match the S66 D9 reader, which rounds
    // with Math.Round(x, 2) (default MidpointRounding.ToEven; BalanceEndpoints ~line 902). Using
    // AwayFromZero here would diverge `over_cap` from D9 `expiring` on a .xx5 midpoint. ToEven so
    // ForfeitDays == D9 expiring byte-for-byte (priority 2 / ADR-033 D3 quantity-determinism).
    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);

    // ------------------------------------------------------------------
    // Event-emit + audit_projection sync-in-tx (ADR-018 D3 + ADR-026 D13). The audit row is
    // dispatched FROM this BackgroundService-invoked site via the registry (NOT an endpoint).
    // ------------------------------------------------------------------

    private async Task EmitAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string streamId, IDomainEvent @event, string actorId, string targetOrgId, CancellationToken ct)
    {
        // Enqueue first to capture the outbox_id (aligns audit_projection.outbox_id with the global
        // outbox sequence; ADR-026 D13 ordering), then write the projection row in the SAME tx.
        var outboxId = await _outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

        var auditCtx = new AuditProjectionContext(
            ActorId: actorId,
            ActorPrimaryOrgId: targetOrgId,
            CorrelationId: @event.CorrelationId,
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(@event.OccurredAt, DateTimeKind.Utc)),
            ResolvedTargetOrgId: targetOrgId);

        // Registry dispatch (TASK-6803 mappers register separately via DI; we build against the
        // interface). If no mapper is registered for the event type, TryMap returns null — skip the
        // projection row (the event + outbox still commit; mirrors the backfill skip semantics). The
        // event_id UNIQUE + ON CONFLICT DO NOTHING in the repo keeps a re-run idempotent.
        var rowData = _auditRegistry.TryMap(@event, auditCtx);
        if (rowData is null)
        {
            _logger.LogWarning(
                "No audit-projection mapper registered for settlement event {EventType} ({EventId}); audit_projection row skipped.",
                @event.EventType, @event.EventId);
            return;
        }

        await _auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, rowData, auditCtx, ct);
    }
}

/// <summary>
/// S68 / TASK-6804 — the §21/§24/§34 partition of a closed vacation year (the legal core; ADR-033
/// D5/D10). PURE result of <see cref="VacationSettlementService.Partition"/>. All day-counts are
/// rounded to the <c>NUMERIC(6,2)</c> storage precision. <see cref="ForfeitDays"/> equals the S66
/// D9 <c>expiring</c> figure (the over-cap §34-candidate bucket).
/// </summary>
public sealed record SettlementPartition(
    decimal Disposable,
    decimal UnderCap,
    decimal OverCap,
    decimal TransferDays,
    decimal PayoutDays,
    decimal ForfeitDays);

/// <summary>
/// S68 / TASK-6804 — the outcome of <see cref="VacationSettlementService.SettleAsync"/>. Either a
/// fresh settlement (with its persisted row + partition) or an idempotent no-op (an active
/// settlement already existed — the in-lock re-check or the 23505 single-settle backstop).
/// </summary>
public sealed record SettlementOutcome
{
    /// <summary><c>true</c> when this call produced a new settlement; <c>false</c> on the idempotent no-op.</summary>
    public required bool DidSettle { get; init; }

    /// <summary>The active settlement row (freshly inserted, or the pre-existing one on a no-op).</summary>
    public required VacationSettlementRow Row { get; init; }

    /// <summary>The computed partition — present only when <see cref="DidSettle"/> is <c>true</c>.</summary>
    public SettlementPartition? Partition { get; init; }

    public static SettlementOutcome Settled(VacationSettlementRow row, SettlementPartition partition) =>
        new() { DidSettle = true, Row = row, Partition = partition };

    public static SettlementOutcome AlreadySettled(VacationSettlementRow existing) =>
        new() { DidSettle = false, Row = existing, Partition = null };
}
