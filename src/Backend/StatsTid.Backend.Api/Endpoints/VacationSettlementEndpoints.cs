using System.Globalization;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S68 / TASK-6806 (ADR-033 D8/D10). The vacation-settlement HR/operator surface — three
/// concerns, all <c>HROrAbove</c> + <see cref="OrgScopeValidator"/> (cross-org binding is
/// load-bearing per the <c>EmployeeProfileEndpoints</c> / <c>EntitlementEligibilityEndpoints</c>
/// precedent: the policy proves role + scope SHAPE but does NOT bind the actor to the target
/// employee's organisation; FAIL-001 — the validator uses <c>FindAll</c>, not <c>FindFirst</c>,
/// on the scope claim):
///
/// <list type="number">
///   <item><description>
///     <b>POST / PUT /api/vacation-transfer-agreements/{employeeId}</b> — the §21 stk.2 written
///     transfer-agreement record. Admin-strict If-Match (ADR-019 D2): POST creates (no If-*),
///     PUT edits-in-place (<c>If-Match: "&lt;version&gt;"</c>). Legal/state guards (Codex Step-0b):
///     VACATION-only; <c>agreement_date</c> ≤ the 31-Dec <b>Copenhagen</b> deadline of the
///     ferieafholdelsesperiode; <c>0 ≤ transfer_days ≤ carryover_max</c> (the statutory transfer
///     ceiling); REJECT (409) when an ACTIVE settlement already exists for the year (cannot agree a
///     transfer for an already-settled year). All writes ride ONE tx (the agreement row + its audit
///     row via <see cref="VacationTransferAgreementRepository"/>).
///   </description></item>
///   <item><description>
///     <b>POST /api/vacation-settlements/{employeeId}/{entitlementType}/{entitlementYear}/resolve</b>
///     — the D10 manual completion of a <c>PENDING_REVIEW</c> settlement. THREE outcomes, EACH ONE
///     atomic tx under the ADR-032 D4 employee advisory lock (SPRINT-71 R12, lock FIRST → in-lock
///     re-read) + an ADR-019 If-Match CAS winner guard (a concurrent loser gets 409 with NO
///     double-emit):
///       <list type="bullet">
///         <item><description><b>FORFEIT (§34):</b> CAS <c>PENDING_REVIEW → SETTLED</c> + set
///         <c>forfeit_days</c> + <c>review_disposition=FORFEIT</c>, emit
///         <see cref="VacationForfeitedToFeriefond"/> (outbox) + the ADR-026 audit_projection row,
///         all in the tx. 422-blocked on <c>trigger=TERMINATION</c> rows (SPRINT-70 R5).</description></item>
///         <item><description><b>DEFER (suspected §22 feriehindring):</b> CAS sets
///         <c>review_disposition=DEFER</c> + bumps <c>version</c> + audit; the row STAYS
///         <c>PENDING_REVIEW</c> (impediment modeling is slice 4). NOT a full resolution.
///         422-blocked on <c>trigger=TERMINATION</c> rows (SPRINT-70 R5).</description></item>
///         <item><description><b>WAIVED (S71 slice 3b, SPRINT-71 R5 / owner D-C):</b> waive-in-full
///         of the §7-shaped over-taken claim on a negative-pre-clamp <c>trigger=TERMINATION</c>
///         <c>PENDING_REVIEW</c> row (the S70 shape: the claim = the forfeit-FLAG on
///         <c>forfeit_days</c>). CAS <c>PENDING_REVIEW → SETTLED</c> + <c>review_disposition=WAIVED</c>
///         + <c>claim_disposition_days = the flagged quantity</c> (from the ROW, never recomputed)
///         + CLEARS the transient forfeit flag (<c>forfeit_days → 0</c> — a waived claim must never
///         read as §34 forfeiture), emit <see cref="TerminationClaimWaived"/> + the ADR-026 row, all
///         in the tx. NO <c>carryover_in</c> write; NO payroll line (R9 — waiver has no consumer).
///         The §7 <b>MODREGNING</b> (deduct-in-full) verb is PARKED behind the SLS-dialogue task
///         (slice Step-0 gate (i)) — a MODREGNING-shaped attempt gets a dedicated 422.</description></item>
///       </list>
///   </description></item>
///   <item><description>
///     <b>GET /api/vacation-settlements/payout-pending</b> (§24 — SETTLED rows with
///     <c>payout_days &gt; 0</c> still awaiting the S69 payroll line, not yet manually reconciled)
///     + <b>POST /api/vacation-settlements/{employeeId}/{entitlementType}/{entitlementYear}/reconcile-payout</b>
///     — an audited CAS write of <c>payout_reconciled_at/by</c> so the S69 emitter can skip a
///     manually-handled bucket. The GET is org-scope-filtered (GlobalAdmin sees all;
///     LocalAdmin/HR see only their subtree per <see cref="OrgScopeValidator.GetAccessibleOrgsAsync"/>).
///   </description></item>
/// </list>
///
/// <para>
/// Settlement is GLOBAL (ADR-025 D6 — no per-institution override). The §34 forfeit event carries
/// no actor on its payload (it is <c>DomainEventBase</c>); the actor + correlation are threaded into
/// the <see cref="AuditProjectionContext"/> directly, mirroring the
/// <c>VacationSettlementService.EmitAsync</c> dispatch shape (ADR-026 D2 — the endpoint resolves the
/// employee→org lookup BEFORE the pure mapper runs).
/// </para>
/// </summary>
public static class VacationSettlementEndpoints
{
    /// <summary>The only entitlement_type a §21 transfer agreement may be recorded for. CARE_DAY /
    /// SENIOR_DAY / SPECIAL_HOLIDAY are NOT §21-transferable here (the &gt;4-week-tranche §21 stk.2
    /// rule is VACATION-specific). Case-sensitive Ordinal — entitlement_type is an identifier.</summary>
    private const string VacationType = "VACATION";

    /// <summary>The state-sector §21 stk.2 statutory transfer ceiling, used ONLY as a fail-closed
    /// fallback when no dated VACATION <c>carryover_max</c> resolves for the employee's agreement
    /// (so the cap guard always enforces). The authoritative cap is re-pinned at settle time from
    /// the immutable snapshot's <c>CarryoverMax</c> (ADR-033 D3) — this endpoint guard is a ceiling,
    /// not the settlement valuation.</summary>
    private const decimal StatutoryTransferCapFallback = 5m;

    public static WebApplication MapVacationSettlementEndpoints(this WebApplication app)
    {
        MapTransferAgreementWrite(app, isCreate: true);   // POST  (create)
        MapTransferAgreementWrite(app, isCreate: false);  // PUT   (edit-in-place)
        MapResolve(app);                                  // POST  /resolve (D10 FORFEIT / DEFER)
        MapPayoutPendingList(app);                        // GET   /payout-pending (§24)
        MapReconcilePayout(app);                          // POST  /reconcile-payout (§24 marker)
        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 1. POST / PUT /api/vacation-transfer-agreements/{employeeId}  — §21 stk.2 record (ADR-033 D8)
    // ═══════════════════════════════════════════════════════════════════════
    private static void MapTransferAgreementWrite(WebApplication app, bool isCreate)
    {
        // POST = first-create (no If-*; sets ETag on 201). PUT = edit-in-place (admin-strict
        // If-Match, ADR-019 D2). Shared body + guards; the create/update fork rides `isCreate`.
        Func<
            string, SetTransferAgreementRequest,
            VacationTransferAgreementRepository, VacationSettlementRepository,
            EntitlementConfigRepository, UserAgreementCodeRepository, UserRepository,
            DbConnectionFactory, OrgScopeValidator, HttpContext, CancellationToken,
            Task<IResult>> handler = async (
            employeeId, body,
            transferRepo, settlementRepo,
            configRepo, agreementCodeRepo, userRepo,
            connectionFactory, scopeValidator, context, ct) =>
        {
            var actor = context.GetActorContext();

            // Cross-org binding (HROrAbove proves role + scope shape only; bind to the target's org).
            // S76 / TASK-7600 B1 (completeness-sweep find): LocalHR floor — the ADMITTING scope
            // must itself be HR+, else a mixed-role HR@A + Leader@B actor writes a §21 transfer
            // agreement for a B employee via the non-admin Leader scope (the mixed-role leak class).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Guard 1 (legal scope) — VACATION-only.
            if (!string.Equals(body.EntitlementType, VacationType, StringComparison.Ordinal))
            {
                return Results.UnprocessableEntity(new
                {
                    error = $"entitlementType '{body.EntitlementType}' is not §21-transferable.",
                    settable = new[] { VacationType },
                    hint = "The §21 stk.2 written-transfer agreement applies to the >4-week VACATION tranche only.",
                });
            }

            // Guard 2 (statutory floor) — transfer_days >= 0 (the DB CHECK also enforces this; we
            // reject early with a clear 422 rather than surfacing a raw 23514).
            if (body.TransferDays < 0m)
                return Results.UnprocessableEntity(new { error = "transferDays must be >= 0." });

            // Admin-strict If-Match — PUT requires it (412/428); POST must NOT carry one.
            long expectedVersion = 0;
            if (!isCreate)
            {
                if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out expectedVersion, out var headerError))
                    return Results.Json(new { error = headerError }, statusCode: 428);
            }

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // Guard 3 (§21 stk.2 deadline) — agreement_date must be ON OR BEFORE the 31-Dec
            // COPENHAGEN deadline of the ferieafholdelsesperiode.
            //
            // BLOCKER 1 (Codex Step-5a) — VACATION entitlement-years are keyed by the ferieår START
            // year E: ferieår E = [Sep 1 E .. Aug 31 E+1] (reset_month 9) or [Jan 1 E .. Dec 31 E]
            // (reset_month 1). The §21 stk.2 deadline is 31 Dec of the ferieår-END year — 31 Dec E+1
            // for reset-9, 31 Dec E for reset-1 — NOT 31 Dec E. The prior `new DateOnly(EntitlementYear,
            // 12, 31)` rejected valid reset-9 agreements a full year early AND resolved the dated cap at
            // the wrong date. The deadline is now derived from the SAME reset_month geometry the close
            // service uses (SettlementCloseService.IsBoundaryPassed / VacationSettlementService.
            // CaptureSnapshotAsync): reset_month 1 → ferieår-end Dec 31 E; else → (ferieaarStart).AddYears(1)
            // .AddDays(-1). The dated VACATION cap is resolved on the SAME chain the settle-time snapshot
            // uses (agreement-at + ok_version-at the ferieår START — WARNING fix below), so the guard cap
            // equals the snapshot's CarryoverMax. Compared against the Copenhagen business clock (NOT UTC)
            // so an agreement at 23:30 on 31 Dec Copenhagen-time is in-deadline even if UTC has rolled.
            var copenhagenToday = DateOnly.FromDateTime(NowCopenhagen());
            var (deadline, cap) = await ResolveDeadlineAndCapAsync(
                configRepo, agreementCodeRepo, userRepo, employeeId, body.EntitlementYear, ct);

            // The agreement_date is recorded as-stated, but it may never be claimed AFTER the
            // Copenhagen business clock has passed the deadline (no retroactive in-period claim once
            // the deadline is wall-clock-past), and the stated date itself may not exceed the deadline.
            if (body.AgreementDate > deadline)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "agreementDate is after the §21 stk.2 transfer deadline.",
                    agreementDate = body.AgreementDate,
                    deadline,
                    hint = $"The written §21 transfer for ferieår {body.EntitlementYear} must be dated on or before {deadline:yyyy-MM-dd} (Copenhagen).",
                });
            }
            // WARNING (Codex Step-5a) — no future-date guard. A future-dated agreement (still ≤ deadline)
            // was accepted; you cannot record an agreement dated after the Copenhagen business clock.
            if (body.AgreementDate > copenhagenToday)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "agreementDate is in the future (after the Copenhagen business clock).",
                    agreementDate = body.AgreementDate,
                    copenhagenToday,
                    hint = "A §21 transfer agreement cannot be recorded with a future date.",
                });
            }
            if (copenhagenToday > deadline)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "The §21 stk.2 transfer deadline has passed (Copenhagen business clock).",
                    deadline,
                    copenhagenToday,
                    hint = "A §21 transfer cannot be recorded after 31 Dec of the ferieafholdelsesperiode.",
                });
            }

            // Guard 4 (statutory cap) — transfer_days <= carryover_max. The dated VACATION carryover_max
            // was resolved above on the settle-time chain (ferieår-start agreement + ok_version), fail-
            // closed to the statutory 5 when no dated config resolves (the guard always enforces).
            if (body.TransferDays > cap)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "transferDays exceeds the statutory transfer cap (carryover_max).",
                    transferDays = body.TransferDays,
                    cap,
                });
            }

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // BLOCKER 2 (Codex Step-5a) — serialize the §21 write against the SettlementCloseService
                // poller. Acquire the SAME per-employee advisory lock the settlement pass holds
                // (pg_advisory_xact_lock(hashtext('employee-' || id)); VacationSettlementService.SettleAsync
                // / EmployeeConsumptionLock) FIRST, before the active-settlement check and the agreement
                // write. Without it, the poller could settle the year BETWEEN the GetActiveAsync check and
                // the agreement commit → a post-settlement agreement omitted from the snapshot. Held to
                // commit (transaction-scoped, auto-released), so the poller cannot settle mid-write.
                await EmployeeConsumptionLock.AcquireAsync(conn, tx, employeeId, ct);

                // Guard 5 (reject-post-settlement) — read the ACTIVE settlement INSIDE the tx so the
                // check observes the same snapshot as the write. If a non-REVERSED settlement already
                // exists for (emp, VACATION, year), a §21 transfer is moot (the year is closed) → 409.
                var activeSettlement = await settlementRepo.GetActiveAsync(
                    conn, tx, employeeId, VacationType, body.EntitlementYear, ct);
                if (activeSettlement is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "An active vacation settlement already exists for this year.",
                        employeeId,
                        entitlementType = VacationType,
                        entitlementYear = body.EntitlementYear,
                        settlementState = activeSettlement.SettlementState,
                        hint = "A §21 transfer cannot be agreed for an already-settled ferieår.",
                    }, statusCode: 409);
                }

                var record = new VacationTransferAgreement
                {
                    EmployeeId = employeeId,
                    EntitlementYear = body.EntitlementYear,
                    EntitlementType = VacationType,
                    TransferDays = body.TransferDays,
                    AgreementDate = body.AgreementDate,
                    RecordedBy = actorId,
                };

                VacationTransferAgreement persisted;
                if (isCreate)
                {
                    try
                    {
                        // Repo Insert appends the CREATED audit row in this same (conn, tx).
                        persisted = await transferRepo.InsertAsync(conn, tx, record, actorId, actorRole, ct);
                    }
                    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
                    {
                        // The PK (employee, year, type) already has an agreement — POST is create-only.
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "A §21 transfer agreement already exists for this (employee, year).",
                            hint = "Use PUT with If-Match: \"<version>\" to edit it.",
                        }, statusCode: 409);
                    }
                }
                else
                {
                    try
                    {
                        // Repo Update does the If-Match version check + bump + UPDATED audit, in-tx.
                        persisted = await transferRepo.UpdateAsync(
                            conn, tx, record, expectedVersion, actorId, actorRole, ct);
                    }
                    catch (OptimisticConcurrencyException ex)
                    {
                        await tx.RollbackAsync(ct);
                        // actualVersion == null when the row does not exist at all → 404 is clearer
                        // than 412 (nothing to edit); a real version mismatch → 412.
                        if (ex.ActualVersion is null)
                            return Results.NotFound(new { error = "No §21 transfer agreement exists to edit. Use POST to create." });
                        return Results.Json(new
                        {
                            error = "Concurrency precondition failed",
                            expectedVersion = ex.ExpectedVersion,
                            actualVersion = ex.ActualVersion,
                        }, statusCode: 412);
                    }
                }

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{persisted.Version}\"";
                var payload = new
                {
                    employeeId = persisted.EmployeeId,
                    entitlementYear = persisted.EntitlementYear,
                    entitlementType = persisted.EntitlementType,
                    transferDays = persisted.TransferDays,
                    agreementDate = persisted.AgreementDate,
                    recordedBy = persisted.RecordedBy,
                    version = persisted.Version,
                };
                return isCreate ? Results.Created($"/api/vacation-transfer-agreements/{employeeId}", payload) : Results.Ok(payload);
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        };

        if (isCreate)
            app.MapPost("/api/vacation-transfer-agreements/{employeeId}", handler).RequireAuthorization("HROrAbove");
        else
            app.MapPut("/api/vacation-transfer-agreements/{employeeId}", handler).RequireAuthorization("HROrAbove");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. POST .../resolve  — D10 manual completion (FORFEIT / DEFER / WAIVED), ADR-019 CAS
    //    (ADR-033 D10; SPRINT-71 R5 added the slice-3b WAIVED verb; §7 MODREGNING is PARKED)
    // ═══════════════════════════════════════════════════════════════════════
    private static void MapResolve(WebApplication app)
    {
        app.MapPost("/api/vacation-settlements/{employeeId}/{entitlementType}/{entitlementYear:int}/resolve", async (
            string employeeId,
            string entitlementType,
            int entitlementYear,
            ResolveSettlementRequest body,
            VacationSettlementRepository settlementRepo,
            EntitlementBalanceRepository balanceRepo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<VacationForfeitedToFeriefond> forfeitAuditMapper,
            IAuditProjectionMapper<TerminationClaimWaived> waiverAuditMapper,
            IAuditProjectionMapper<FeriehindringTransferred> feriehindringAuditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S70 / TASK-7003 (SPRINT-70 R9c allowlist) — terminated-INCLUSIVE validator: an HR
            // operator must be able to resolve a deactivated leaver's PENDING_REVIEW settlement
            // (the S68 B2 fix). HROrAbove policy + subtree binding unchanged; only the target
            // resolution stops filtering is_active. The S71 WAIVED verb rides the SAME surface,
            // so it inherits this authorization shape verbatim (SPRINT-71 owner D-B).
            // S76 B1 fix-forward (cycle 2): LocalHR per-scope floor — a mixed HR@A + Leader@B JWT
            // can no longer manually resolve (FORFEIT/DEFER/WAIVE) an ACTIVE B settlement row via
            // the Leader scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var disposition = (body.Disposition ?? string.Empty).Trim().ToUpperInvariant();

            // S71 / TASK-7103 (SPRINT-71 gate (i)) — the §7 MODREGNING (deduct-in-full) verb is
            // PARKED behind the SLS-dialogue task: the §7 stk.1 outstanding-pay cap is not
            // confirmably SLS-enforceable on a day-count input, so the deduction verb, its event
            // (TerminationModregningApplied) and its SLS_TBD_S7 line are all undefined in 3b. A
            // §7-shaped attempt gets THIS dedicated 422 (before any row state is consulted — the
            // verb does not exist regardless of the row).
            if (disposition == "MODREGNING")
            {
                return Results.UnprocessableEntity(new
                {
                    error = "The §7 MODREGNING (deduct-in-full) disposition is PARKED pending the SLS dialogue.",
                    disposition,
                    hint = "Slice 3b ships the waiver branch only (SPRINT-71 slice Step-0 gate (i)): " +
                           "whether SLS nets/caps a §7 termination set-off against final pay — or needs " +
                           "a pre-capped quantity — is unverified, so the deduction verb and its event " +
                           "are parked behind the SLS-dialogue task. Use WAIVED to waive the claim in " +
                           "full, or leave the row parked PENDING_REVIEW.",
                });
            }

            // Disposition must be exactly FORFEIT, DEFER, WAIVED (S71 R5) or FERIEHINDRING (S79 R3).
            if (disposition is not ("FORFEIT" or "DEFER" or "WAIVED" or "FERIEHINDRING"))
                return Results.UnprocessableEntity(new { error = "disposition must be 'FORFEIT', 'DEFER', 'WAIVED' or 'FERIEHINDRING'." });

            // Admin-strict If-Match — the ADR-019 CAS token; a concurrent loser gets 412/409, never
            // a double-emit.
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // S71 / TASK-7103 (SPRINT-71 R12) — ONE employee advisory lock across ALL settlement
                // mutation paths: acquire the SAME ADR-032 D4 lock the close service, the reversal
                // service and the reconcile endpoint take, FIRST, so the row read below is an IN-LOCK
                // re-read that observes any state a concurrent reversal/settlement committed (the S70
                // B1 lesson: every pre-check predicate re-evaluated under the lock). Applied to the
                // WHOLE resolve surface — FORFEIT/DEFER included — which serializes resolve-vs-reversal
                // for every verb (the R12 race list). Held to commit (transaction-scoped).
                await EmployeeConsumptionLock.AcquireAsync(conn, tx, employeeId, ct);

                // Read the ACTIVE settlement in-tx, under the lock (the CAS subject).
                var current = await settlementRepo.GetActiveAsync(
                    conn, tx, employeeId, entitlementType, entitlementYear, ct);
                if (current is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "No active settlement found for this (employee, type, year)." });
                }

                if (disposition is "FORFEIT" or "DEFER" or "FERIEHINDRING")
                {
                    // S70 / TASK-7004 (SPRINT-70 R5, Step-0b cycle-4; SPRINT-71 R5 KEEPS this; S79 R3
                    // EXTENDS it to FERIEHINDRING) — trigger=TERMINATION rows are NOT
                    // FORFEIT/DEFER/FERIEHINDRING-resolvable: FORFEIT would emit
                    // VacationForfeitedToFeriefond, a materially FALSE event for an over-taken-claim
                    // (the negative-pre-clamp PENDING_REVIEW row holds a §7-shaped CLAIM, not §34
                    // forfeiture); FERIEHINDRING rescues from a §34 remainder a TERMINATION row does
                    // not carry (its forfeit_days is the |pre-clamp| §7 claim flag, never a §34
                    // candidate). Slice 3b's resolution for TERMINATION rows is the WAIVED verb below
                    // (§7 MODREGNING stays parked). Guard placed AFTER row resolution, BEFORE any
                    // disposition logic.
                    if (string.Equals(current.Trigger, "TERMINATION", StringComparison.Ordinal))
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "A TERMINATION settlement cannot be resolved with FORFEIT, DEFER or FERIEHINDRING.",
                            trigger = current.Trigger,
                            settlementState = current.SettlementState,
                            hint = "FORFEIT would emit a materially false VacationForfeitedToFeriefond for an " +
                                   "over-taken-claim, and FERIEHINDRING rescues from a §34 remainder a " +
                                   "TERMINATION row does not carry; a negative-pre-clamp TERMINATION row holds " +
                                   "a §7-shaped claim, not §34 forfeiture. Use WAIVED to waive the claim in full " +
                                   "(the §7 MODREGNING deduction is parked pending the SLS dialogue).",
                        });
                    }
                }
                else // WAIVED (S71 / TASK-7103, SPRINT-71 R5)
                {
                    // The waiver applies ONLY to the parked §7-shaped TERMINATION claim — a YEAR_END
                    // row's PENDING_REVIEW remainder is a §34-vs-§22 adjudication (FORFEIT/DEFER),
                    // never a waivable claim.
                    if (!string.Equals(current.Trigger, "TERMINATION", StringComparison.Ordinal))
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "WAIVED applies only to TERMINATION settlements.",
                            trigger = current.Trigger,
                            settlementState = current.SettlementState,
                            hint = "The waive-in-full disposition resolves the §7-shaped over-taken claim on a " +
                                   "negative-pre-clamp TERMINATION row. A YEAR_END PENDING_REVIEW remainder is " +
                                   "adjudicated via FORFEIT (§34) or DEFER (suspected §22).",
                        });
                    }
                }

                // Only a PENDING_REVIEW row is resolvable (a SETTLED row is already complete).
                if (!string.Equals(current.SettlementState, "PENDING_REVIEW", StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Settlement is not in PENDING_REVIEW; nothing to resolve.",
                        settlementState = current.SettlementState,
                    }, statusCode: 409);
                }

                if (disposition == "WAIVED")
                {
                    // S71 R5 — the claim IS the S70 forfeit-FLAG (|pre-clamp| stamped on forfeit_days
                    // at the PENDING_REVIEW close). No flag ⇒ no §7-shaped claim ⇒ nothing to waive
                    // (defense-in-depth: R5 pins PENDING_REVIEW iff negative pre-clamp, so a flagless
                    // TERMINATION PENDING_REVIEW row should not exist — fail closed, never invent a
                    // zero-quantity waiver record).
                    if (current.ForfeitDays <= 0m)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "No §7-shaped claim flag on this TERMINATION row; nothing to waive.",
                            forfeitDays = current.ForfeitDays,
                            settlementState = current.SettlementState,
                            hint = "A waivable claim is the |pre-clamp| forfeit-FLAG a negative-pre-clamp " +
                                   "TERMINATION close stamped on forfeit_days (SPRINT-70 R5).",
                        });
                    }

                    // The waived quantity comes from the ROW, never from the caller (R5: never
                    // recomputed, never operator-supplied) — reject any body quantity outright.
                    if (body.ForfeitDays is not null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "WAIVED takes no forfeitDays — the waived quantity is the row's flagged claim.",
                            flaggedClaim = current.ForfeitDays,
                        });
                    }
                }

                // S79 / TASK-7901 (SPRINT-79 R3) — FERIEHINDRING input validation (BEFORE the If-Match
                // precondition, mirroring the WAIVED body-quantity guards: a malformed-input 422 must
                // not depend on the version cursor). The §22 day-count (impededDays) + the durable
                // reason are operator-supplied; both are validated against the in-lock row.
                decimal impededDays = 0m;
                string feriehindringReason = string.Empty;
                if (disposition == "FERIEHINDRING")
                {
                    // The reason is REQUIRED + non-empty (the durable §22 impediment rationale — the
                    // FeriehindringTransferred event field + the queryable feriehindring_reason mirror;
                    // the 7900 paired CHECK requires it on a FERIEHINDRING row).
                    feriehindringReason = (body.Reason ?? string.Empty).Trim();
                    if (feriehindringReason.Length == 0)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "FERIEHINDRING requires a non-empty reason (the §22 impediment rationale).",
                            hint = "Record the impediment (sickness/barsel etc.) — it is the durable audit record.",
                        });
                    }

                    // The §22 day-count must be supplied (impededDays) and positive.
                    if (body.ImpededDays is not { } supplied)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "FERIEHINDRING requires impededDays (the §22 impeded day-count to rescue).",
                        });
                    }
                    impededDays = supplied;

                    // Scale guard (S79 Step-5a Codex BLOCKER): impededDays must fit the NUMERIC(6,2)
                    // persisted bucket EXACTLY. Without this a sub-cent value (e.g. 0.001) passes the
                    // range guard but rounds to 0.00 in feriehindring_transfer_days, while the in-memory
                    // event + carryover compose from the unrounded value → row/event/carryover divergence
                    // (the "same day never both transfers and forfeits" determinism breaks). Reject > 2dp.
                    if (decimal.Round(impededDays, 2) != impededDays)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "impededDays must have at most 2 decimal places (the NUMERIC(6,2) day-count scale).",
                            impededDays,
                        });
                    }

                    // 0 < impededDays <= min(current.ForfeitDays, 20) — BOTH the flagged §34-candidate
                    // remainder AND the independent §22 4-week / 20-day statutory cap. A clean 422 (never
                    // a raw CHECK violation): impededDays > ForfeitDays would drive forfeit_days negative
                    // (the §22-FIRST/§34-residual rule), and impededDays > 20 exceeds the §22 ceiling.
                    const decimal Section22StatutoryCap = 20m;
                    var rescueCeiling = Math.Min(current.ForfeitDays, Section22StatutoryCap);
                    if (impededDays <= 0m || impededDays > rescueCeiling)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "impededDays must satisfy 0 < impededDays <= min(forfeit_days, 20).",
                            impededDays,
                            flaggedRemainder = current.ForfeitDays,
                            section22Cap = Section22StatutoryCap,
                            rescueCeiling,
                            hint = "The §22 rescue is bounded by BOTH the flagged §34-candidate remainder " +
                                   "(the same day never both transfers and forfeits) AND the independent §22 " +
                                   "4-week / 20-day statutory cap. A partial rescue leaves the residual as §34.",
                        });
                    }

                    // S79 Step-7a BLOCKER 2 — a leaver's deferred-disposition row is NOT a §34
                    // candidate. SettleLeaverDeferredDispositionAsync (VacationSettlementService.cs:657,
                    // R4) writes a leaver's OTHER ferieår as trigger=YEAR_END PENDING_REVIEW with
                    // forfeit_days = the FULL disposable (a FLAG, NOT a computed §34 bucket) and stamps
                    // the snapshot DeferredDisposition marker (VacationSettlementSnapshot.cs:192). The
                    // TERMINATION re-pin above does not catch it (trigger is YEAR_END), so FERIEHINDRING
                    // would treat the full-disposable flag as a §34 remainder and wrongly transfer/forfeit
                    // an UNPARTITIONED leaver disposition. Detect the marker on the in-lock row's snapshot
                    // and refuse 422 (before any mutation). FORFEIT/DEFER remain the valid resolutions for
                    // those rows (unchanged).
                    var feriehindringValidationSnapshot = DeserializeSnapshot(current.SnapshotJson);
                    if (feriehindringValidationSnapshot?.DeferredDisposition == true)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.UnprocessableEntity(new
                        {
                            error = "FERIEHINDRING cannot resolve a leaver deferred-disposition row (forfeit_days is a full-disposable flag, not a §34 candidate).",
                            trigger = current.Trigger,
                            settlementState = current.SettlementState,
                            forfeitDays = current.ForfeitDays,
                            hint = "A leaver's other-ferieår YEAR_END row carries the FULL unpartitioned " +
                                   "disposable as a flag (SPRINT-70 R4), not a computed §34 remainder. Resolve " +
                                   "it via FORFEIT (§34) or DEFER (suspected §22) — not the §22 rescue, which " +
                                   "presupposes a partitioned §34-candidate remainder.",
                        });
                    }
                }

                // If-Match precondition on the row version (ADR-019).
                if (current.Version != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = current.Version,
                    }, statusCode: 412);
                }

                var previousData = SerializeSettlementForAudit(current);

                if (disposition == "DEFER")
                {
                    // DEFER (suspected §22) — CAS sets review_disposition=DEFER + bumps version; the
                    // row STAYS PENDING_REVIEW (impediment modeling is slice 4). The CAS WHERE-clause
                    // re-asserts version so a concurrent winner makes this a 0-row no-op → 409.
                    var deferred = await CasUpdateSettlementAsync(conn, tx,
                        current,
                        newState: "PENDING_REVIEW",
                        reviewDisposition: "DEFER",
                        forfeitDays: current.ForfeitDays,
                        expectedVersion, ct);
                    if (deferred is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.Json(new { error = "Concurrent update — refresh and retry.", actualVersion = (long?)null }, statusCode: 409);
                    }

                    await settlementRepo.AppendAuditAsync(conn, tx, deferred, "UPDATED",
                        previousData, SerializeSettlementForAudit(deferred),
                        versionBefore: current.Version, versionAfter: deferred.Version,
                        actorId, actorRole, ct);

                    await tx.CommitAsync(ct);

                    context.Response.Headers.ETag = $"\"{deferred.Version}\"";
                    return Results.Ok(new
                    {
                        employeeId,
                        entitlementType,
                        entitlementYear,
                        sequence = deferred.Sequence,
                        settlementState = deferred.SettlementState,
                        reviewDisposition = deferred.ReviewDisposition,
                        resolved = false,
                        version = deferred.Version,
                        hint = "Deferred as suspected §22 feriehindring; remains PENDING_REVIEW until slice 4.",
                    });
                }

                if (disposition == "WAIVED")
                {
                    // ── WAIVED (S71 / TASK-7103; SPRINT-71 R5, owner D-C waive-in-full) ──
                    // ONE atomic tx, already under the R12 advisory lock: CAS PENDING_REVIEW →
                    // SETTLED with review_disposition=WAIVED + claim_disposition_days = the flagged
                    // quantity (from the ROW — never recomputed) + forfeit_days → 0 (CLEAR the
                    // transient flag: the claim must never read as §34 forfeiture; the 7100
                    // bidirectional CHECK enforces the disposition⟺quantity pairing), then emit
                    // TerminationClaimWaived (outbox) + the ADR-026 audit_projection row + the
                    // settlement-table audit row. NO carryover_in write (the restated 3a invariant —
                    // no resolve disposition writes carryover); NO payroll line / staging (R9 —
                    // waiver has no consumer).
                    var waivedDays = current.ForfeitDays;
                    var waived = await CasUpdateSettlementAsync(conn, tx,
                        current,
                        newState: "SETTLED",
                        reviewDisposition: "WAIVED",
                        forfeitDays: 0m,
                        expectedVersion, ct,
                        claimDispositionDays: waivedDays);
                    if (waived is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.Json(new { error = "Concurrent update — refresh and retry.", actualVersion = (long?)null }, statusCode: 409);
                    }

                    // Employee → org resolution for the TENANT_TARGETED audit row (ADR-026 D2; the
                    // is_active-unfiltered in-tx read — a deactivated leaver is the NORMAL case here).
                    var waiverOrgId = await ResolvePrimaryOrgIdInTxAsync(conn, tx, employeeId, ct);
                    if (waiverOrgId is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = $"Employee '{employeeId}' not found." });
                    }

                    // The 7101 payload shape: settlement-ROW identity + SettlementSequence (R2 —
                    // settlement sequence, not export sequence) + WaivedDays mirroring
                    // claim_disposition_days.
                    var waivedEvent = new TerminationClaimWaived
                    {
                        EmployeeId = employeeId,
                        EntitlementType = entitlementType,
                        EntitlementYear = entitlementYear,
                        SettlementSequence = waived.Sequence,
                        WaivedDays = waivedDays,
                        CorrelationId = actor.CorrelationId,
                    };

                    var waiverStreamId = $"employee-{employeeId}";
                    var waiverOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, waiverStreamId, waivedEvent, ct);
                    // ActorPrimaryOrgId = the OPERATOR's org; ResolvedTargetOrgId = the employee's
                    // (the request-endpoint / lifecycle-writer convention — a parent-org HR actor
                    // must not be attributed the target's organizational provenance).
                    var waiverAuditCtx = new AuditProjectionContext(
                        ActorId: actorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: waivedEvent.CorrelationId,
                        OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(waivedEvent.OccurredAt, DateTimeKind.Utc)),
                        ResolvedTargetOrgId: waiverOrgId);
                    var waiverRowData = waiverAuditMapper.Map(waivedEvent, waiverAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, waivedEvent.EventId, waiverOutboxId, waivedEvent.EventType, waiverRowData, waiverAuditCtx, ct);

                    await settlementRepo.AppendAuditAsync(conn, tx, waived, "UPDATED",
                        previousData, SerializeSettlementForAudit(waived),
                        versionBefore: current.Version, versionAfter: waived.Version,
                        actorId, actorRole, ct);

                    await tx.CommitAsync(ct);

                    context.Response.Headers.ETag = $"\"{waived.Version}\"";
                    return Results.Ok(new
                    {
                        employeeId,
                        entitlementType,
                        entitlementYear,
                        sequence = waived.Sequence,
                        settlementState = waived.SettlementState,
                        reviewDisposition = waived.ReviewDisposition,
                        claimDispositionDays = waived.ClaimDispositionDays,
                        forfeitDays = waived.ForfeitDays, // 0 — the cleared transient flag
                        resolved = true,
                        version = waived.Version,
                    });
                }

                if (disposition == "FERIEHINDRING")
                {
                    // ── FERIEHINDRING (§22, S79 / TASK-7901; SPRINT-79 R1/R3/R8/R12) ──
                    // The §22 impediment RESCUE of the impeded tranche from the §34 forfeiture bucket.
                    // ONE atomic tx, already under the R12 advisory lock:
                    //   (1) CAS PENDING_REVIEW → SETTLED, review_disposition=FERIEHINDRING, with §22
                    //       FIRST and §34 the RESIDUAL — the same day never both transfers and forfeits:
                    //         feriehindring_transfer_days := impededDays
                    //         forfeit_days                := current.ForfeitDays − impededDays  (>= 0, R3-validated)
                    //       + feriehindring_reason (the durable rationale, 7900 paired CHECK).
                    //   (2) Source-keyed carryover (R1): OVERWRITE next-year carryover_in with
                    //       §21 (transfer_days, already persisted at close) + §22 (impededDays), composed
                    //       from the PERSISTED row buckets — exactly ONE WriteCarryoverInAsync; skip only
                    //       when the sum is zero.
                    //   (3) Emit FeriehindringTransferred (with the reason + snapshot + TransferDays =
                    //       impededDays) + VacationForfeitedToFeriefond for the RESIDUAL forfeit_days ONLY
                    //       IF > 0 (a FULL rescue emits no forfeit event) + the ADR-026 audit rows.
                    // Money stays OUT (R12): §22 is balance-only — NO payroll line / staging. No `used`
                    // mutation. The automated close is unchanged (R8): §34 is never blind-auto-forfeited.

                    // S79 Step-7a BLOCKER 1 — carryover-finality guard (the superseding-side pattern,
                    // VacationSettlementService.cs:386-396). The §22 rescue OVERWRITES next-year
                    // carryover_in (step (2)). If year N+1 ALREADY holds an active (non-REVERSED)
                    // settlement, its snapshot/events are FINAL — a 2024 PENDING_REVIEW row resolved
                    // AFTER the close poll has settled 2025 would silently overwrite 2025's finalized
                    // carryover_in, breaking replay/finality. The §22 rescue cannot be applied once the
                    // next year is final; refuse LOUDLY (rollback + clean 409), corrupting nothing. The
                    // in-tx GetActiveAsync excludes REVERSED rows, so this reads exactly the active
                    // next-year settlement, and runs UNDER the held R12 advisory lock (in-lock re-read),
                    // BEFORE the CAS mutation / any emit.
                    var nextYearActive = await settlementRepo.GetActiveAsync(
                        conn, tx, employeeId, entitlementType, entitlementYear + 1, ct);
                    if (nextYearActive is not null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "The next entitlement year is already settled; its carryover_in is final and cannot be overwritten by a §22 rescue.",
                            failure = "NextYearAlreadySettled",
                            nextYear = entitlementYear + 1,
                            nextYearSettlementState = nextYearActive.SettlementState,
                            nextYearTrigger = nextYearActive.Trigger,
                            hint = "FERIEHINDRING re-states next year's carryover_in (§21 + §22). Once year " +
                                   "N+1 holds an active settlement its snapshot/events are final; the §22 " +
                                   "rescue must not retroactively mutate the settled next-year balance " +
                                   "(consistent with the superseding-carryover finality guard).",
                        }, statusCode: 409);
                    }

                    var residualForfeit = current.ForfeitDays - impededDays; // >= 0 by the R3 ceiling guard
                    var rescued = await CasUpdateSettlementAsync(conn, tx,
                        current,
                        newState: "SETTLED",
                        reviewDisposition: "FERIEHINDRING",
                        forfeitDays: residualForfeit,
                        expectedVersion, ct,
                        feriehindringTransferDays: impededDays,
                        feriehindringReason: feriehindringReason);
                    if (rescued is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.Json(new { error = "Concurrent update — refresh and retry.", actualVersion = (long?)null }, statusCode: 409);
                    }

                    // Employee → org for the ADR-026 TENANT_TARGETED audit rows + the snapshot for the
                    // emitted events (the is_active-unfiltered in-tx read — a YEAR_END PENDING_REVIEW row
                    // may belong to a since-deactivated leaver, the FORFEIT-path precedent).
                    var feriehindringOrgId = await ResolvePrimaryOrgIdInTxAsync(conn, tx, employeeId, ct);
                    if (feriehindringOrgId is null)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = $"Employee '{employeeId}' not found." });
                    }

                    // (2) Source-keyed carryover (R1) — compose §21 + §22 from the PERSISTED row buckets
                    // (NOT a live re-derivation). The §21 component (transfer_days) was already written at
                    // close if > 0; this overwrite RE-STATES §21 + §22 idempotently (WriteCarryoverInAsync
                    // is a SET, not a +=). Skip only when the combined sum is zero (nothing to write —
                    // never clobber an independent producer's row with 0; the §21-zero/§22-positive case
                    // still writes). seedQuota = the snapshot's annual quota (the §21 path's convention).
                    var feriehindringSnapshot = DeserializeSnapshot(rescued.SnapshotJson);
                    var combinedCarryover = rescued.TransferDays + impededDays; // §21 (persisted) + §22 (rescued)
                    if (combinedCarryover > 0m)
                    {
                        await balanceRepo.WriteCarryoverInAsync(
                            conn, tx, employeeId, entitlementType, entitlementYear + 1,
                            carryoverDays: combinedCarryover,
                            seedQuota: feriehindringSnapshot?.AnnualQuota ?? 0m, ct);
                    }

                    // (3a) FeriehindringTransferred — the §22 rescue fact + the durable reason (R1/R3).
                    var feriehindringStreamId = $"employee-{employeeId}";
                    var feriehindringEvent = new FeriehindringTransferred
                    {
                        EmployeeId = employeeId,
                        EntitlementType = entitlementType,
                        EntitlementYear = entitlementYear,
                        Sequence = rescued.Sequence,
                        Snapshot = feriehindringSnapshot,
                        TransferDays = impededDays,
                        FeriehindringReason = feriehindringReason,
                        CorrelationId = actor.CorrelationId,
                    };
                    var feriehindringOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, feriehindringStreamId, feriehindringEvent, ct);
                    // ActorPrimaryOrgId = the OPERATOR's org; ResolvedTargetOrgId = the employee's (the
                    // request-endpoint convention — a parent-org HR actor must not be attributed the
                    // target's organizational provenance).
                    var feriehindringAuditCtx = new AuditProjectionContext(
                        ActorId: actorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: feriehindringEvent.CorrelationId,
                        OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(feriehindringEvent.OccurredAt, DateTimeKind.Utc)),
                        ResolvedTargetOrgId: feriehindringOrgId);
                    var feriehindringRowData = feriehindringAuditMapper.Map(feriehindringEvent, feriehindringAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, feriehindringEvent.EventId, feriehindringOutboxId,
                        feriehindringEvent.EventType, feriehindringRowData, feriehindringAuditCtx, ct);

                    // (3b) VacationForfeitedToFeriefond for the RESIDUAL §34 — ONLY when > 0 (a FULL
                    // rescue emits NO forfeit event). Reuses the existing §34 mapper (R3).
                    if (residualForfeit > 0m)
                    {
                        var residualForfeitEvent = new VacationForfeitedToFeriefond
                        {
                            EmployeeId = employeeId,
                            EntitlementType = entitlementType,
                            EntitlementYear = entitlementYear,
                            Sequence = rescued.Sequence,
                            Snapshot = feriehindringSnapshot,
                            ForfeitDays = residualForfeit,
                            CorrelationId = actor.CorrelationId,
                        };
                        var residualOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, feriehindringStreamId, residualForfeitEvent, ct);
                        var residualAuditCtx = new AuditProjectionContext(
                            ActorId: actorId,
                            ActorPrimaryOrgId: actor.OrgId,
                            CorrelationId: residualForfeitEvent.CorrelationId,
                            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(residualForfeitEvent.OccurredAt, DateTimeKind.Utc)),
                            ResolvedTargetOrgId: feriehindringOrgId);
                        var residualRowData = forfeitAuditMapper.Map(residualForfeitEvent, residualAuditCtx);
                        await auditRepo.InsertAsync(conn, tx, residualForfeitEvent.EventId, residualOutboxId,
                            residualForfeitEvent.EventType, residualRowData, residualAuditCtx, ct);
                    }

                    // The settlement-table audit row (version transition; mirrors the Phase-2 shape).
                    await settlementRepo.AppendAuditAsync(conn, tx, rescued, "UPDATED",
                        previousData, SerializeSettlementForAudit(rescued),
                        versionBefore: current.Version, versionAfter: rescued.Version,
                        actorId, actorRole, ct);

                    await tx.CommitAsync(ct);

                    context.Response.Headers.ETag = $"\"{rescued.Version}\"";
                    return Results.Ok(new
                    {
                        employeeId,
                        entitlementType,
                        entitlementYear,
                        sequence = rescued.Sequence,
                        settlementState = rescued.SettlementState,
                        reviewDisposition = rescued.ReviewDisposition,
                        feriehindringTransferDays = rescued.FeriehindringTransferDays,
                        feriehindringReason = rescued.FeriehindringReason,
                        forfeitDays = rescued.ForfeitDays, // the §34 residual (0 on a full rescue)
                        carryoverIn = combinedCarryover,   // §21 + §22 written to next year (R1)
                        resolved = true,
                        version = rescued.Version,
                    });
                }

                // FORFEIT (§34) — CAS PENDING_REVIEW → SETTLED, set forfeit_days +
                // review_disposition=FORFEIT, emit VacationForfeitedToFeriefond, write the audit
                // rows. forfeit_days := the flagged §34-candidate remainder already on the row (we do
                // NOT re-derive; the snapshot is authoritative — ADR-033 D2). The flagged remainder was
                // stamped on forfeit_days == the snapshot over_cap at the PENDING_REVIEW close.
                //
                // WARNING (Codex + Reviewer Step-5a) — FORFEIT forfeits the WHOLE flagged remainder.
                // An unbounded operator override (the prior `body.ForfeitDays ?? current.ForfeitDays`,
                // guarded only by >= 0) could (a) EXCEED current.ForfeitDays → over-forfeiture / a wrong
                // §34 Feriefonden amount, or (b) be BELOW it while the CAS still marks the whole row
                // SETTLED → silently losing the §22 remainder. Partial §34-vs-§22 splitting is slice 4;
                // a suspected §22 remainder is handled by DEFER, not a partial FORFEIT. If an override is
                // supplied it must therefore equal the flagged remainder EXACTLY; reject 422 otherwise.
                var forfeitDays = current.ForfeitDays;
                if (body.ForfeitDays is { } requested && requested != forfeitDays)
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "forfeitDays must equal the flagged §34 remainder (FORFEIT forfeits the whole remainder).",
                        requested,
                        flaggedRemainder = forfeitDays,
                        hint = "A partial forfeit (suspected §22 remainder) is not supported in slice 1a — use DEFER. Omit forfeitDays to forfeit the flagged remainder, or send the exact flagged value.",
                    });
                }

                var settled = await CasUpdateSettlementAsync(conn, tx,
                    current,
                    newState: "SETTLED",
                    reviewDisposition: "FORFEIT",
                    forfeitDays: forfeitDays,
                    expectedVersion, ct);
                if (settled is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Concurrent update — refresh and retry.", actualVersion = (long?)null }, statusCode: 409);
                }

                // Resolve the employee's org for the ADR-026 TENANT_TARGETED audit_projection row
                // (the mapper is pure; the endpoint resolves the lookup — ADR-026 D2).
                //
                // WARNING (Reviewer Step-5a) — do NOT use userRepo.GetByIdAsync here: it filters
                // is_active = TRUE and throws InvalidOperationException → 500 for a SINCE-DEACTIVATED
                // employee. A YEAR_END PENDING_REVIEW row created while the employee was active, then the
                // employee deactivated before an operator resolves it, is realistic — forfeiting it must
                // still succeed. Read primary_org_id directly (no is_active filter) on the tx connection.
                // The §21/§24 paths' own GetByIdAsync degradations are noted in the report (the §21 cap
                // path already falls back to the statutory cap on a null user — acceptable).
                var primaryOrgId = await ResolvePrimaryOrgIdInTxAsync(conn, tx, employeeId, ct);
                if (primaryOrgId is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = $"Employee '{employeeId}' not found." });
                }

                // The §34 event carries the immutable settle-time snapshot verbatim (ADR-033 D3) —
                // deserialized from the row, NOT re-derived.
                var snapshot = DeserializeSnapshot(settled.SnapshotJson);
                var forfeitEvent = new VacationForfeitedToFeriefond
                {
                    EmployeeId = employeeId,
                    EntitlementType = entitlementType,
                    EntitlementYear = entitlementYear,
                    Sequence = settled.Sequence,
                    Snapshot = snapshot,
                    ForfeitDays = forfeitDays,
                    CorrelationId = actor.CorrelationId,
                };

                // Atomic-outbox emit + audit_projection (same tx; mirrors VacationSettlementService.EmitAsync).
                var streamId = $"employee-{employeeId}";
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, forfeitEvent, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actorId,
                    ActorPrimaryOrgId: primaryOrgId,
                    CorrelationId: forfeitEvent.CorrelationId,
                    OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(forfeitEvent.OccurredAt, DateTimeKind.Utc)),
                    ResolvedTargetOrgId: primaryOrgId);
                var rowData = forfeitAuditMapper.Map(forfeitEvent, auditCtx);
                await auditRepo.InsertAsync(conn, tx, forfeitEvent.EventId, outboxId, forfeitEvent.EventType, rowData, auditCtx, ct);

                // The settlement-table audit row (version transition; mirrors the Phase-2 shape).
                await settlementRepo.AppendAuditAsync(conn, tx, settled, "UPDATED",
                    previousData, SerializeSettlementForAudit(settled),
                    versionBefore: current.Version, versionAfter: settled.Version,
                    actorId, actorRole, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{settled.Version}\"";
                return Results.Ok(new
                {
                    employeeId,
                    entitlementType,
                    entitlementYear,
                    sequence = settled.Sequence,
                    settlementState = settled.SettlementState,
                    reviewDisposition = settled.ReviewDisposition,
                    forfeitDays = settled.ForfeitDays,
                    resolved = true,
                    version = settled.Version,
                });
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3a. GET /api/vacation-settlements/payout-pending  — §24 awaiting-the-S69-line set
    // ═══════════════════════════════════════════════════════════════════════
    private static void MapPayoutPendingList(WebApplication app)
    {
        app.MapGet("/api/vacation-settlements/payout-pending", async (
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Org-scope filter — GlobalAdmin (null sentinel) sees all; LocalAdmin/HR see only their
            // subtree; Employee/unscoped → empty (the GetAccessibleOrgsAsync contract). The settled
            // rows are joined to users.primary_org_id and filtered on it.
            // S76 / TASK-7600 B1 (completeness-sweep find): HROrAbove read → LocalHR floor on the
            // accessible-org union — pre-fix a mixed HR@A + Leader@B actor had B's settlement rows
            // unioned in via the non-admin Leader scope (the same picker-leak class 7600 closed).
            var accessibleOrgs = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
            if (accessibleOrgs is { Count: 0 })
                return Results.Ok(new { items = Array.Empty<object>(), count = 0 });

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);

            // SETTLED + payout_days > 0 + not-yet-reconciled. accessibleOrgs == null ⇒ GlobalAdmin,
            // no org filter; else filter to the subtree. Parameterised array (no string interpolation
            // of identifiers); the @orgFilterOff flag short-circuits the org predicate for GlobalAdmin.
            await using var cmd = new NpgsqlCommand(
                """
                SELECT s.employee_id, s.entitlement_type, s.entitlement_year, s.sequence,
                       s.payout_days, s.version, s.created_at, u.primary_org_id
                FROM vacation_settlements s
                JOIN users u ON u.user_id = s.employee_id
                WHERE s.settlement_state = 'SETTLED'
                  AND s.payout_days > 0
                  AND s.payout_reconciled_at IS NULL
                  AND (@orgFilterOff OR u.primary_org_id = ANY(@accessibleOrgs))
                ORDER BY s.created_at ASC, s.employee_id ASC
                """, conn);
            var globalAdmin = accessibleOrgs is null;
            cmd.Parameters.AddWithValue("orgFilterOff", globalAdmin);
            cmd.Parameters.Add(new NpgsqlParameter("accessibleOrgs", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = (object?)(accessibleOrgs?.ToArray()) ?? Array.Empty<string>(),
            });

            var items = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                items.Add(new
                {
                    employeeId = reader.GetString(0),
                    entitlementType = reader.GetString(1),
                    entitlementYear = reader.GetInt32(2),
                    sequence = reader.GetInt32(3),
                    payoutDays = reader.GetDecimal(4),
                    version = reader.GetInt64(5),
                    settledAt = reader.GetDateTime(6),
                    primaryOrgId = reader.GetString(7),
                });
            }

            return Results.Ok(new { items, count = items.Count });
        }).RequireAuthorization("HROrAbove");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3b. POST .../reconcile-payout  — §24 manual-reconciliation marker (audited CAS)
    // ═══════════════════════════════════════════════════════════════════════
    private static void MapReconcilePayout(WebApplication app)
    {
        app.MapPost("/api/vacation-settlements/{employeeId}/{entitlementType}/{entitlementYear:int}/reconcile-payout", async (
            string employeeId,
            string entitlementType,
            int entitlementYear,
            VacationSettlementRepository settlementRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S70 / TASK-7003 (SPRINT-70 R9c allowlist) — terminated-INCLUSIVE validator: the
            // manual §24 reconcile marker must remain operable for a deactivated leaver's
            // settled row (the S68 B2 fix). HROrAbove policy + subtree binding unchanged.
            // S76 B1 fix-forward (cycle 2): LocalHR per-scope floor — a mixed HR@A + Leader@B JWT
            // can no longer set the §24 reconcile marker on an ACTIVE B row via the Leader scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match — the CAS token (ADR-019 D2).
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // S69 TASK-6905 (ADR-033 D4 / ADR-032 D4) — claim/reconcile MUTUAL EXCLUSION.
                // Acquire the employee advisory lock FIRST (held to commit; the SAME key the Payroll
                // SettlementExportEmitter + the Backend close service take). Without it, the operator's
                // reconcile and the emitter's §24 claim could both succeed (double-pay): the emitter
                // skips when payout_reconciled_at is set, this endpoint refuses (409) when the emitter
                // already staged the bucket — but only the shared lock makes "set marker" and "stage
                // line" mutually exclusive. Across {close, reconcile, emitter} exactly one disposition
                // per (identity, sequence, bucket).
                await using (var lockCmd = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("employeeId", employeeId);
                    await lockCmd.ExecuteScalarAsync(ct);
                }

                var current = await settlementRepo.GetActiveAsync(
                    conn, tx, employeeId, entitlementType, entitlementYear, ct);
                if (current is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "No active settlement found for this (employee, type, year)." });
                }

                // S70 / TASK-7004 (SPRINT-70 R5) — the §24 reconcile-payout marker does NOT apply to
                // trigger=TERMINATION rows in 3a: a TERMINATION row carries NO §24 bucket (all bucket
                // columns are zero on SETTLED; the crystallized §26 quantity lives in the snapshot, and
                // the launch-time payout is recorded OUTSIDE the system). The payout_days <= 0 check
                // below would already 409 these zero-bucket rows — this explicit 422 makes the contract
                // explicit (pinned).
                if (string.Equals(current.Trigger, "TERMINATION", StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "The §24 reconcile-payout marker does not apply to TERMINATION settlements in slice 3a.",
                        trigger = current.Trigger,
                        hint = "A TERMINATION settlement has no §24 payout bucket; the crystallized §26 day-count " +
                               "lives in the immutable snapshot and the launch-time payout is the manual fallback " +
                               "recorded outside the system (the in-system disposition channel is slice 3b).",
                    });
                }

                // Only a SETTLED row with an un-reconciled payout bucket is markable.
                if (!string.Equals(current.SettlementState, "SETTLED", StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Settlement is not SETTLED.", settlementState = current.SettlementState }, statusCode: 409);
                }
                if (current.PayoutDays <= 0m)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Settlement has no §24 payout bucket to reconcile." }, statusCode: 409);
                }
                if (current.PayoutReconciledAt is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Payout already reconciled.",
                        reconciledAt = current.PayoutReconciledAt,
                        reconciledBy = current.PayoutReconciledBy,
                    }, statusCode: 409);
                }

                // S69 TASK-6905 — line/checkpoint-absence pre-check (the other half of the XOR). Under
                // the held lock, refuse (409) if the Payroll emitter has ALREADY staged the §24 bucket:
                // a settlement_export_lines row for this active sequence's AUTO_PAYOUT_24 bucket, OR a
                // settlement_payroll_inbox row PROCESSED for the same identity/bucket. The machine
                // claimed it; the operator must NOT also reconcile it (that is the double-pay the lock
                // prevents from racing and this check prevents from completing).
                await using (var stagedCmd = new NpgsqlCommand(
                    """
                    SELECT
                        EXISTS (
                            SELECT 1 FROM settlement_export_lines
                            WHERE employee_id = @employeeId AND entitlement_type = @entitlementType
                              AND entitlement_year = @entitlementYear AND sequence = @sequence
                              AND bucket = 'AUTO_PAYOUT_24')
                        OR EXISTS (
                            SELECT 1 FROM settlement_payroll_inbox
                            WHERE employee_id = @employeeId AND entitlement_type = @entitlementType
                              AND entitlement_year = @entitlementYear AND sequence = @sequence
                              AND bucket = 'AUTO_PAYOUT_24' AND processing_status = 'PROCESSED')
                    """, conn, tx))
                {
                    stagedCmd.Parameters.AddWithValue("employeeId", employeeId);
                    stagedCmd.Parameters.AddWithValue("entitlementType", entitlementType);
                    stagedCmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
                    stagedCmd.Parameters.AddWithValue("sequence", current.Sequence);
                    var alreadyStaged = await stagedCmd.ExecuteScalarAsync(ct) is true;
                    if (alreadyStaged)
                    {
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "The §24 payout line was already staged by the settlement-export emitter; " +
                                    "manual reconciliation is not permitted (reconcile XOR machine-claim).",
                            employeeId,
                            entitlementType,
                            entitlementYear,
                            sequence = current.Sequence,
                        }, statusCode: 409);
                    }
                }

                if (current.Version != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = current.Version,
                    }, statusCode: 412);
                }

                var previousData = SerializeSettlementForAudit(current);

                // CAS marker write — set payout_reconciled_at/by (paired-nullable; satisfies the
                // vacation_settlements_payout_reconciled_paired CHECK) + bump version, re-asserting
                // version in the WHERE so a concurrent winner makes this a 0-row no-op → 409.
                long newVersion;
                DateTime reconciledAt;
                await using (var cmd = new NpgsqlCommand(
                    """
                    UPDATE vacation_settlements SET
                        payout_reconciled_at = NOW(),
                        payout_reconciled_by = @actorId,
                        version = version + 1,
                        updated_at = NOW()
                    WHERE employee_id = @employeeId
                      AND entitlement_type = @entitlementType
                      AND entitlement_year = @entitlementYear
                      AND sequence = @sequence
                      AND version = @expectedVersion
                    RETURNING version, payout_reconciled_at
                    """, conn, tx))
                {
                    cmd.Parameters.AddWithValue("actorId", actorId);
                    cmd.Parameters.AddWithValue("employeeId", employeeId);
                    cmd.Parameters.AddWithValue("entitlementType", entitlementType);
                    cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
                    cmd.Parameters.AddWithValue("sequence", current.Sequence);
                    cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
                    await using var reader = await cmd.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct))
                    {
                        await reader.DisposeAsync();
                        await tx.RollbackAsync(ct);
                        return Results.Json(new { error = "Concurrent update — refresh and retry." }, statusCode: 409);
                    }
                    newVersion = reader.GetInt64(0);
                    reconciledAt = reader.GetDateTime(1);
                }

                var reconciledRow = current with
                {
                    PayoutReconciledAt = reconciledAt,
                    PayoutReconciledBy = actorId,
                    Version = newVersion,
                };
                await settlementRepo.AppendAuditAsync(conn, tx, reconciledRow, "UPDATED",
                    previousData, SerializeSettlementForAudit(reconciledRow),
                    versionBefore: current.Version, versionAfter: newVersion,
                    actorId, actorRole, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{newVersion}\"";
                return Results.Ok(new
                {
                    employeeId,
                    entitlementType,
                    entitlementYear,
                    sequence = current.Sequence,
                    payoutReconciledAt = reconciledAt,
                    payoutReconciledBy = actorId,
                    version = newVersion,
                });
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove");
    }

    // ───────────────────────────── helpers ─────────────────────────────

    /// <summary>
    /// CAS UPDATE of the settlement state-machine row (re-asserting <paramref name="expectedVersion"/>
    /// in the WHERE so a concurrent winner yields a 0-row no-op → caller returns 409, no double-emit).
    /// Returns the persisted row, or <c>null</c> on the 0-row CAS loss. Used by all four D10/R5/S79
    /// outcomes (FORFEIT sets SETTLED, DEFER keeps PENDING_REVIEW, WAIVED sets SETTLED + clears the
    /// flag + records <paramref name="claimDispositionDays"/>, FERIEHINDRING sets SETTLED + nets
    /// forfeit_days + records <paramref name="feriehindringTransferDays"/> + <paramref name="feriehindringReason"/>)
    /// — the §24 reconcile path has its own inline UPDATE (different column set).
    /// <paramref name="claimDispositionDays"/> is non-null ONLY for WAIVED (the 7100 bidirectional
    /// CHECK pairs it with MODREGNING/WAIVED); <paramref name="feriehindringReason"/> is non-null ONLY
    /// for FERIEHINDRING (the 7900 bidirectional CHECK pairs the reason with FERIEHINDRING). FORFEIT/DEFER
    /// write NULL for both, which is a no-op on a PENDING_REVIEW row (the pairing CHECKs forbid them there).
    /// </summary>
    private static async Task<VacationSettlementRow?> CasUpdateSettlementAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        VacationSettlementRow current, string newState, string reviewDisposition, decimal forfeitDays,
        long expectedVersion, CancellationToken ct, decimal? claimDispositionDays = null,
        decimal feriehindringTransferDays = 0m, string? feriehindringReason = null)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE vacation_settlements SET
                settlement_state = @newState,
                review_disposition = @reviewDisposition,
                forfeit_days = @forfeitDays,
                claim_disposition_days = @claimDispositionDays,
                feriehindring_transfer_days = @feriehindringTransferDays,
                feriehindring_reason = @feriehindringReason,
                version = version + 1,
                updated_at = NOW()
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
              AND sequence = @sequence
              AND version = @expectedVersion
            RETURNING version
            """, conn, tx);
        cmd.Parameters.AddWithValue("newState", newState);
        cmd.Parameters.AddWithValue("reviewDisposition", reviewDisposition);
        cmd.Parameters.AddWithValue("forfeitDays", forfeitDays);
        cmd.Parameters.Add(new NpgsqlParameter("claimDispositionDays", NpgsqlTypes.NpgsqlDbType.Numeric)
        {
            Value = (object?)claimDispositionDays ?? DBNull.Value,
        });
        cmd.Parameters.AddWithValue("feriehindringTransferDays", feriehindringTransferDays);
        cmd.Parameters.Add(new NpgsqlParameter("feriehindringReason", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = (object?)feriehindringReason ?? DBNull.Value,
        });
        cmd.Parameters.AddWithValue("employeeId", current.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementType", current.EntitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", current.EntitlementYear);
        cmd.Parameters.AddWithValue("sequence", current.Sequence);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        if (scalar is null)
            return null;
        var newVersion = Convert.ToInt64(scalar);
        return current with
        {
            SettlementState = newState,
            ReviewDisposition = reviewDisposition,
            ForfeitDays = forfeitDays,
            ClaimDispositionDays = claimDispositionDays,
            FeriehindringTransferDays = feriehindringTransferDays,
            FeriehindringReason = feriehindringReason,
            Version = newVersion,
        };
    }

    /// <summary>The seeded VACATION <c>reset_month</c> geometry — the ferieår starts 1 Sep (reset_month
    /// 9; ADR-030/S65 verified). Used ONLY as the fail-closed fallback when no config resolves for the
    /// employee (a null/inactive user, or a configless agreement) so the §21 deadline still derives
    /// from the correct ferieår-end geometry rather than the wrong 31-Dec-E.</summary>
    private const int VacationResetMonthFallback = 9;

    /// <summary>
    /// BLOCKER 1 + the cap/ok-version WARNINGs (Codex Step-5a) — derive the §21 stk.2 deadline from the
    /// ferieår-END year (NOT the START year <paramref name="entitlementYear"/>) and resolve the dated
    /// VACATION <c>carryover_max</c> on the SAME settle-time chain
    /// <see cref="VacationSettlementService"/> uses, so the cap guard equals the snapshot's
    /// <c>CarryoverMax</c> byte-for-byte.
    ///
    /// <para>
    /// Geometry (mirrors <c>SettlementCloseService.IsBoundaryPassed</c> /
    /// <c>VacationSettlementService.CaptureSnapshotAsync</c>): for ferieår E,
    /// reset_month 1 → ferieår [Jan 1 E .. Dec 31 E], deadline 31 Dec E; else (e.g. VACATION
    /// reset_month 9) → ferieår [Sep 1 E .. Aug 31 E+1], deadline 31 Dec (E+1). The OK version is
    /// resolved at the ferieår START via <see cref="OkVersionResolver.ResolveVersion(DateOnly)"/>
    /// (the dated config key — NOT the user's CURRENT ok_version), and the dated config is read at
    /// the ferieår-start agreement + that OK version. Fail-closed to the statutory ceiling when no
    /// dated config resolves (the cap guard always enforces). Pure read; no I/O on the write tx.
    /// </para>
    /// </summary>
    /// <returns>(<c>deadline</c> = 31 Dec of the ferieår-end year; <c>cap</c> = the dated VACATION
    /// carryover_max, or the statutory fallback).</returns>
    private static async Task<(DateOnly Deadline, decimal Cap)> ResolveDeadlineAndCapAsync(
        EntitlementConfigRepository configRepo,
        UserAgreementCodeRepository agreementCodeRepo,
        UserRepository userRepo,
        string employeeId, int entitlementYear, CancellationToken ct)
    {
        var user = await userRepo.GetByIdAsync(employeeId, ct);

        // Discover reset_month from the live config (keyed on the user's live agreement + ok_version) so
        // the ferieår geometry is correct for reset_month 1 vs 9. On a null/inactive/configless user,
        // fall back to the seeded VACATION geometry (reset_month 9) — the deadline still derives from the
        // ferieår-END year (the BLOCKER-1 correction) rather than the wrong 31-Dec-E.
        int resetMonth = VacationResetMonthFallback;
        if (user is not null)
        {
            var liveConfig = await configRepo.GetCurrentOpenAsync(
                VacationType, user.AgreementCode, user.OkVersion, ct);
            if (liveConfig is not null)
                resetMonth = liveConfig.ResetMonth;
        }

        // S80 / TASK-8001 (R10) — the ferieår START + the §21 31-Dec deadline of the ferieår-END year
        // now come from the shared EntitlementPeriodResolver (BEHAVIOR-IDENTICAL for VACATION: reset_month
        // 9 ⇒ ferieaarStart 1 Sep E, deadline 31 Dec E+1; reset_month 1 ⇒ 1 Jan E / 31 Dec E). This guard
        // is VACATION-only (the §21 stk.2 transfer applies to the >4-week VACATION tranche — see Guard 1
        // above), so the SPECIAL_HOLIDAY geometry never reaches here.
        var vacationPeriod = EntitlementPeriodResolver.ResolveForYear(VacationType, resetMonth, entitlementYear);
        DateOnly ferieaarStart = vacationPeriod.AccrualStart;
        var deadline = vacationPeriod.Boundary;

        if (user is null)
            return (deadline, StatutoryTransferCapFallback);

        // Dated cap on the settle-time chain: agreement-at + ok_version-at the ferieår START.
        var okVersion = OkVersionResolver.ResolveVersion(ferieaarStart);
        var agreementCode = await agreementCodeRepo.GetByUserIdAtAsync(employeeId, ferieaarStart, ct)
            ?? user.AgreementCode;
        var config = await configRepo.GetByTypeAtAsync(
            VacationType, agreementCode, okVersion, ferieaarStart, ct);
        return (deadline, config?.CarryoverMax ?? StatutoryTransferCapFallback);
    }

    /// <summary>
    /// WARNING (Reviewer Step-5a) — in-tx read of <c>users.primary_org_id</c> WITHOUT the
    /// <c>is_active</c> filter, so the FORFEIT audit-context resolution succeeds for a since-deactivated
    /// employee (a PENDING_REVIEW row created while active, resolved after deactivation). Returns
    /// <c>null</c> only when the user row genuinely does not exist (caller maps 404 — never a 500).
    /// </summary>
    private static async Task<string?> ResolvePrimaryOrgIdInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT primary_org_id FROM users WHERE user_id = @employeeId", conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    /// <summary>Copenhagen wall-clock now — the §21 stk.2 deadline is a Copenhagen business date,
    /// not UTC. Tries the IANA id first (works cross-platform on .NET 8 ICU), then the Windows id,
    /// then a fixed +01:00 fallback so the resolution never throws on an exotic host.</summary>
    private static DateTime NowCopenhagen()
    {
        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"); }
        catch (TimeZoneNotFoundException)
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"); }
            catch (TimeZoneNotFoundException) { return DateTime.UtcNow.AddHours(1); }
        }
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
    }

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private static VacationSettlementSnapshot? DeserializeSnapshot(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;
        try { return JsonSerializer.Deserialize<VacationSettlementSnapshot>(snapshotJson, SnapshotJsonOptions); }
        catch (JsonException) { return null; }
    }

    /// <summary>Audit payload for a settlement row (the version-transition previous/new data). Mirrors
    /// the per-row field set; the immutable snapshot is carried as raw JSON.</summary>
    private static string SerializeSettlementForAudit(VacationSettlementRow row) =>
        JsonSerializer.Serialize(new
        {
            row.EmployeeId,
            row.EntitlementType,
            row.EntitlementYear,
            row.Sequence,
            row.SettlementState,
            row.Trigger,
            row.TransferDays,
            row.PayoutDays,
            row.ForfeitDays,
            row.ReviewDisposition,
            // S71 / TASK-7103 (R5) — the §7/waiver resolved claim quantity (null for every
            // FORFEIT/DEFER/reconcile transition; the WAIVED audit record must carry it).
            row.ClaimDispositionDays,
            // S79 / TASK-7901 (R1/R3) — the §22 feriehindring rescued bucket + durable reason (0/null
            // for every non-FERIEHINDRING transition; the FERIEHINDRING audit record must carry them).
            row.FeriehindringTransferDays,
            row.FeriehindringReason,
            PayoutReconciledAt = row.PayoutReconciledAt,
            row.PayoutReconciledBy,
            row.Version,
        }, SnapshotJsonOptions);

    // ───────────────────────────── request DTOs ─────────────────────────────

    /// <summary>POST/PUT §21 transfer-agreement body. employeeId is a route param; recorded_by is
    /// server-stamped from the JWT actor.</summary>
    private sealed record SetTransferAgreementRequest
    {
        public required int EntitlementYear { get; init; }
        public required string EntitlementType { get; init; }
        public required decimal TransferDays { get; init; }
        public required DateOnly AgreementDate { get; init; }
    }

    /// <summary>POST /resolve body — the D10/R5/S79 disposition (FORFEIT / DEFER / WAIVED /
    /// FERIEHINDRING; MODREGNING is 422-parked pending the SLS dialogue) + an optional explicit
    /// forfeit_days (FORFEIT only — must equal the flagged remainder exactly; WAIVED rejects it, the
    /// waived quantity always comes from the row) + the §22 FERIEHINDRING inputs
    /// (<see cref="ImpededDays"/> the impeded day-count to rescue + <see cref="Reason"/> the required
    /// non-empty impediment rationale; ignored for the other dispositions).</summary>
    private sealed record ResolveSettlementRequest
    {
        public required string Disposition { get; init; }
        public decimal? ForfeitDays { get; init; }

        /// <summary>S79 (SPRINT-79 R3) — the §22 impeded day-count to rescue from the §34 forfeiture
        /// bucket (FERIEHINDRING only; 0 &lt; impededDays &lt;= min(forfeit_days, 20)).</summary>
        public decimal? ImpededDays { get; init; }

        /// <summary>S79 (SPRINT-79 R3) — the durable §22 impediment rationale (FERIEHINDRING only;
        /// required non-empty — the FeriehindringTransferred event field + feriehindring_reason mirror).</summary>
        public string? Reason { get; init; }
    }
}
