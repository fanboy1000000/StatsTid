using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Validation;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class TimeEndpoints
{
    public static WebApplication MapTimeEndpoints(this WebApplication app)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("StatsTid.Backend.Api.Endpoints.TimeEndpoints");

        // ── Time Entries ──

        app.MapPost("/api/time-entries", async (
            RegisterTimeEntryRequest request,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            TimeEntryProjectionRepository timeProjectionRepo,
            // S128 / TASK-12803 — the in-transaction approval-period read (the (conn, tx) overload
            // only; see the in-lock check below).
            ApprovalPeriodRepository approvalRepo,
            // S136 / TASK-13603 — the subject read this endpoint never had (terminated-INCLUSIVE,
            // ADR-040 D3 / SEC-046) + the in-lock employment-window read (the (conn, tx) surface
            // only; the SharedKernel self-managed twin would read OUTSIDE the advisory lock).
            UserRepository userRepo,
            IEmploymentWindowResolverInTx employmentWindowResolver,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
                return Results.Forbid();

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                // ── S124 / TASK-12404 — owner ruling 2026-07-30 ──────────────────────────────
                // "A manager can never edit an employee's registrations. Only HR and admins can."
                //
                // Before this, ANY non-Employee actor whose org-scope covered the target could write
                // another employee's time data — which includes LocalLeader. The floor lifts that to
                // LocalHR-or-above (LocalHR / LocalAdmin / GlobalAdmin) via the EXISTING per-scope
                // roleFloor mechanism (OrgScopeValidator:88-91): a scope below the floor never admits,
                // so a mixed-role actor's LEADER scope cannot carry the write while their HR scope,
                // if any, still can.
                //
                // SELF IS EXEMPT, and that exemption is load-bearing: a LocalLeader is also an
                // employee who registers their OWN time. They are not Employee-role, so they fall
                // through to this scope branch — applying the HR floor unconditionally would have
                // locked every leader out of their own timesheet.
                //
                // READS are untouched. A leader must still review the full month grid of a submitted
                // period (TASK-12403) — this narrows WRITES only.
                //
                // This endpoint WAS the WORSE member of the write class the skema save belongs to: it
                // had NO approval-period status check at all, so before the floor a leader could write
                // an employee's time entry in ANY period state. (S128 / TASK-12803 closed that: the
                // in-lock period check inside the transaction below now enforces the SAME
                // ApprovalPeriodSaveLock the Skema save enforces.)
                //
                // ── S136 / TASK-13603 — TERMINATED-INCLUSIVE validator swap (ADR-040 D3) ─────────
                // Surface 1 of the S70 R9c allowlist extension this task makes (mandatory
                // Security-invariant review): the shared ValidateEmployeeAccessAsync resolves its
                // target ACTIVE-ONLY, so a deactivated leaver was "Target employee not found" (403)
                // even to in-scope HR — locking HR out of the routine final-month correction. The
                // IncludingTerminated validator (same writeFloor semantics for ACTIVE targets, so
                // the S124 floor + self-exemption above are byte-preserved) additionally admits a
                // TERMINATED subject via its R9b/R9f1 gates: only a scope that is ITSELF LocalHR or
                // above may admit one, in both the GLOBAL and the CoversOrg branch. Employee-role
                // actors never reach this branch (handled above); Employee-SHAPED actors (no role /
                // all-Employee scopes) are denied outright by the validator's no-own-data-branch
                // decision (R9b) — strictly fail-closed relative to the old validator.
                var writeFloor = string.Equals(request.EmployeeId, actor.ActorId, StringComparison.Ordinal)
                    ? null
                    : StatsTidRoles.LocalHR;
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
                    actor, request.EmployeeId, writeFloor, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // ── S136 / TASK-13603 — THE SUBJECT READ (surface 2; the SEC-046 closure) ────────────
            // This endpoint never loaded the subject's users row at all, which was SEC-046's live
            // surface: a TERMINATED employee's still-valid JWT (8h lifetime) sailed through the
            // Employee own-data branch above — the only access check that path has — and could
            // register time for ANY date. The read is the S70 R9c terminated-INCLUSIVE repository
            // path (an allowlist extension, in the mandatory Security review) because the ACTIVE-ONLY
            // read would turn every leaver into a 404 and re-break the HR correction flow the
            // validator swap above just fixed.
            var subject = await userRepo.GetByIdIncludingTerminatedAsync(request.EmployeeId, ct);
            if (subject is null)
                return Results.NotFound(new { error = "Employee not found" });

            // ADR-040 D3: the SELF-EXEMPTION YIELDS TO SUBJECT DEACTIVATION. is_active governs
            // login/session for the ACTOR; the SUBJECT's deactivation state selects the role floor
            // for writes — HROrAbove, even when subject == actor. An Employee writing for THEMSELVES
            // is fine while active (unchanged), but a deactivated self is not exempt: Employee-role
            // actors bypass the scope validator entirely (the own-data branch above), so THIS check
            // is the load-bearing SEC-046 closure for them. For non-Employee actors the
            // IncludingTerminated validator already enforced the stronger PER-SCOPE floor (R9f1);
            // this primary-role re-check is defense-in-depth there and can never deny an actor that
            // validator admitted (its R9b gate already requires primary role ≥ LocalHR for a
            // terminated subject).
            //
            // S136 Step-5a (Codex BLOCKER 1) — this pre-transaction check is the ADVISORY FAST
            // PATH (advisory-then-authoritative, the house pattern the approval-period check
            // already follows): it reads on a pooled connection, outside the write transaction
            // and outside EmployeeConsumptionLock, so a deactivation that commits while this
            // request waits on the lock is invisible to it. The AUTHORITATIVE D3 floor is the
            // in-lock subject-state re-check inside the transaction below.
            if (!subject.IsActive &&
                (actor.ActorRole is null || !StatsTidRoles.IsAtLeast(actor.ActorRole, StatsTidRoles.LocalHR)))
            {
                return Results.Json(new
                {
                    error = "Access denied",
                    reason = "Writes for a deactivated employee require LocalHR or above"
                }, statusCode: 403);
            }

            // OK version MUST be resolved server-side from the entry date (ADR-003).
            // The caller-supplied value is advisory only; mismatches are logged but not rejected.
            var resolvedOkVersion = OkVersionResolver.ResolveVersion(request.Date);
#pragma warning disable CS0618 // RegisterTimeEntryRequest.OkVersion is intentionally obsolete/advisory
            var suppliedOkVersion = request.OkVersion;
#pragma warning restore CS0618
            if (!string.Equals(suppliedOkVersion, resolvedOkVersion, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Caller-supplied OkVersion '{Supplied}' differs from server-resolved '{Resolved}' for time entry on {Date} (employee {EmployeeId}). Using resolved value.",
                    suppliedOkVersion, resolvedOkVersion, request.Date, request.EmployeeId);
            }

            var (isValid, error) = RequestValidator.ValidateTimeEntry(request.EmployeeId, request.Hours, request.AgreementCode, resolvedOkVersion);
            if (!isValid)
                return Results.BadRequest(new { error });

            var @event = new TimeEntryRegistered
            {
                EmployeeId = request.EmployeeId,
                Date = request.Date,
                Hours = request.Hours,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                TaskId = request.TaskId,
                ActivityType = request.ActivityType,
                AgreementCode = request.AgreementCode,
                OkVersion = resolvedOkVersion,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId
            };

            // Atomic in-tx write per ADR-018 D3 + S27 Phase 4c.6 projection design
            // (TASK-2707 — re-attempt of S26 TASK-2606 which was reverted because the
            // event-stream-backed GET could not see the just-written event before the
            // OutboxPublisher async drain). Per-event ordering inside the tx:
            //   1. outbox enqueue FIRST → returns the freshly-allocated outbox_id
            //      (TASK-2703 EnqueueAndReturnIdAsync overload).
            //   2. projection INSERT SECOND → consumes the outbox_id so the
            //      time_entries_projection row is keyed to the global outbox sequence
            //      (per-employee monotonic ordering aligned with the global outbox
            //      sequence, see TimeEntryProjectionRepository ORDER BY outbox_id ASC).
            // Both rows commit or roll back together, so the migrated GET below
            // (which reads from the projection) satisfies read-your-write.
            //
            // Stream id literal `employee-{EmployeeId}` unchanged per ADR-018 D6
            // retabulate (TASK-2601) — consolidated employee stream carrying
            // time-entry + absence + entitlement-balance + compliance events.
            var streamId = $"employee-{request.EmployeeId}";

            await using (var conn = connectionFactory.Create())
            {
                await conn.OpenAsync(ct);
                // ── S127 / TASK-12704 — PIN ReadCommitted EXPLICITLY (PAT-015) ────────────────
                // NOT the default overload. This transaction's first statement is the blocking
                // `SELECT pg_advisory_xact_lock(...)` below. Under REPEATABLE READ the snapshot is
                // taken BEFORE the lock is granted, so a winner that commits while we wait — the
                // exact thing we were waiting for — stays invisible and we would proceed on
                // pre-lock state: execution serialized, data un-serialized. ReadCommitted gives
                // each post-lock statement a fresh snapshot, which is what a lock-serialized
                // critical section requires. Same discipline and same comment as the other pinned
                // sites (ReportingLineEndpoints, ReportingLineRepository, SettlementCloseService,
                // ApprovalEndpoints' send/reopen transactions).
                //
                // Corollary (PAT-015): never pass an `ApprovalAuthorityContext` on this
                // transaction — DesignatedApproverAuthorizer.EnsureContextIsSnapshotBound throws
                // unless it gets RepeatableRead or stronger. This handler passes none.
                await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
                try
                {
                    // ── S127 / TASK-12704 — the per-employee advisory lock, FIRST STATEMENT ────
                    // Before ANY read or write in this transaction. `time_entries_projection` is
                    // the ALLOCATED side of the submit-time allocation gate (refinement §1
                    // defect 4): without this lock an unallocated time entry could commit AFTER a
                    // send validates and BEFORE it commits, leaving a month in a manager-visible
                    // state that is already invalid. Enrolling this writer in the SAME
                    // per-employee lock the send command and the Skema save take makes the S127
                    // invariant true for the request path (§2).
                    //
                    // The SAME lock, deliberately — EmployeeConsumptionLock
                    // (`pg_advisory_xact_lock(hashtext('employee-' || id))`, transaction-scoped).
                    // A second advisory lock acquired in a different order by two paths is a
                    // deadlock; there is exactly one.
                    await StatsTid.Backend.Api.Services.EmployeeConsumptionLock.AcquireAsync(
                        conn, tx, request.EmployeeId, ct);

                    // ── S136 Step-5a (Codex BLOCKER 1 — the SEC-046 race) — THE AUTHORITATIVE
                    // SUBJECT-STATE RE-CHECK, IN-LOCK ─────────────────────────────────────────────
                    // The D3 role floor above was decided from the UNLOCKED pre-transaction subject
                    // read (the advisory fast path). The deactivation writers — the employment-date
                    // PUTs' R1 lifecycle and the Step-A settlement flip — commit under this SAME
                    // EmployeeConsumptionLock, so a deactivation can land while this request waits
                    // on the acquire above; without this re-read a still-live Employee token would
                    // write for a just-deactivated subject. Re-read is_active on THIS (conn, tx) —
                    // under the lock, seeing the winner's commit via the ReadCommitted pin — and
                    // re-enforce the SAME D3 floor, same 403 shape as the pre-check.
                    var lockedSubject = await userRepo.GetByIdIncludingTerminatedAsync(
                        conn, tx, request.EmployeeId, ct);
                    if (lockedSubject is null)
                    {
                        // The row vanished between the pre-check and the lock (no production path
                        // hard-deletes users; defensive symmetry with the pre-check's 404).
                        await tx.RollbackAsync(ct);
                        return Results.NotFound(new { error = "Employee not found" });
                    }
                    if (!lockedSubject.IsActive &&
                        (actor.ActorRole is null || !StatsTidRoles.IsAtLeast(actor.ActorRole, StatsTidRoles.LocalHR)))
                    {
                        // No write has been issued yet; rollback explicitly and refuse with the
                        // pre-check's exact 403 shape (one contract, whichever check fires).
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "Access denied",
                            reason = "Writes for a deactivated employee require LocalHR or above"
                        }, statusCode: 403);
                    }

                    // ── S136 / TASK-13603 — THE EMPLOYMENT-WINDOW GATE (ADR-040 D1–D3), IN-LOCK ────
                    // The AUTHORITATIVE check that the entry date lies inside the subject's
                    // employment window, refused date-free via the shared EmploymentWindowGate (ONE
                    // predicate + ONE 422 construction site with the Skema save — see that class for
                    // the do-not-add-date-fields hard rule).
                    //
                    // Placement is the LOCK REGIME (S136 refinement, Codex-B1): the employment-date
                    // PUTs acquire this SAME EmployeeConsumptionLock as their first in-tx statement
                    // and evaluate their strand/re-hire guards inside it — so evaluating the window
                    // HERE, after our acquire, on the (conn, tx) resolver overload, means a
                    // concurrent window-edit/registration pair cannot interleave into out-of-window
                    // data: whichever commits second sees the other's committed state. All three
                    // ingredients are load-bearing (the same PAT-015 story as the approval check
                    // below): in-lock, in-transaction overload (a self-managed read opens a private
                    // connection OUTSIDE the lock), and the ReadCommitted pin above (under
                    // RepeatableRead the snapshot predates the lock grant and this read would miss
                    // the window edit we blocked for).
                    //
                    // IN-TX ORDER RULE (fixed total order, shared verbatim with the Skema save so
                    // two writers can never deadlock): EmployeeConsumptionLock acquisition FIRST,
                    // then the D3 subject-state re-check above, then this window gate, then the
                    // approval-period status check. Access (403) outranks the window, and the window
                    // outranks the approval status deliberately — non-employment is the strongest
                    // fact about a date (the ADR-040 D5 tie-break spirit), so an out-of-window date
                    // in a locked month reports 422 outside_employment_period, not the 409.
                    var windowStatus = await employmentWindowResolver.GetStatusAsync(
                        conn, tx, request.EmployeeId, request.Date, ct);
                    if (StatsTid.Backend.Api.Services.EmploymentWindowGate.IsOutsideEmploymentWindow(windowStatus))
                    {
                        // No write has been issued yet (the enqueue below is this tx's first write);
                        // rollback explicitly and return the shared date-free 422.
                        await tx.RollbackAsync(ct);
                        return StatsTid.Backend.Api.Services.EmploymentWindowGate.OutsideEmploymentPeriod();
                    }

                    // ── S128 / TASK-12803 — THE APPROVAL-STATUS CHECK (S127 FU-D1, owner ruling R3) ──
                    // The lock above stops this write racing INSIDE a send; this check stops it
                    // writing AFTER one — the defect S127 deliberately carried (refinement §2
                    // qualification 3, §4) and S128 closes. It MIRRORS the Skema save byte-for-byte:
                    // the SAME shared predicate + 409 construction site (ApprovalPeriodSaveLock —
                    // EMPLOYEE_APPROVED / APPROVED are locked; DRAFT, REJECTED, legacy SUBMITTED
                    // (ruling R6) and no-row stay writable; status-only, ALL actors including HR).
                    //
                    // Placement (PAT-015): AFTER the lock, BEFORE any write, on the (conn, tx)
                    // IN-TRANSACTION overload — the self-managed overload opens a private connection
                    // that sits outside this transaction and therefore outside the lock, so it would
                    // miss a send that committed while we blocked on the acquire. Seeing that commit
                    // also requires the ReadCommitted pin above; under RepeatableRead the snapshot
                    // predates the lock grant and this read would be a no-op. This in-tx read is the
                    // race the AC-7-style send-wins-then-POST test pins.
                    //
                    // KNOWN RESIDUAL (carried, shared with Skema — SPRINT-128 TASK-12803): the
                    // natural-key probe matches whole-calendar-month period_start/period_end EXACTLY,
                    // so a non-whole-month approval_periods row for this date would not be found and
                    // "no row ⇒ allow" is weaker than it reads. Pre-existing on the Skema save's
                    // identical probe; out of scope here.
                    var monthStart = new DateOnly(request.Date.Year, request.Date.Month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    var inLockPeriod = await approvalRepo.GetByEmployeeAndPeriodAsync(
                        conn, tx, request.EmployeeId, monthStart, monthEnd, ct);
                    if (ApprovalPeriodSaveLock.IsPeriodLockedForSave(inLockPeriod))
                    {
                        // Rollback explicitly before returning the shared 409 — no write has been
                        // issued yet (the enqueue below is this tx's first write).
                        await tx.RollbackAsync(ct);
                        return ApprovalPeriodSaveLock.PeriodLockedForSaveConflict(inLockPeriod!);
                    }

                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);
                    await timeProjectionRepo.InsertAsync(conn, tx, @event, outboxId, ct);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }

            // S120 / TASK-12000 — named record (BYTE-IDENTICAL wire JSON; the 201 receipt).
            return Results.Created($"/api/time-entries/{request.EmployeeId}", new TimeEntryCreatedResponse(
                EventId: @event.EventId,
                StreamId: streamId));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<TimeEntryCreatedResponse>(StatusCodes.Status201Created) // S120 / TASK-12000
        // S128 / TASK-12803 — the period-locked conflict (ApprovalPeriodSaveLock), same shape as
        // the Skema save's 409.
        .Produces(StatusCodes.Status409Conflict);

        app.MapGet("/api/time-entries/{employeeId}", async (
            string employeeId,
            TimeEntryProjectionRepository timeProjectionRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Ownership check for Employee role
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Forbid();

            // For higher roles, verify org scope covers the target employee
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // S27 Phase 4c.6 / TASK-2707: read from time_entries_projection instead of
            // replaying the event stream. The atomic POST above commits the projection
            // row in the same tx as the outbox enqueue, so reads see the just-written
            // entry without waiting for the OutboxPublisher async drain (read-your-write).
            // Full-stream read (no date filter) — uses idx_time_entries_proj_emp_outbox.
            var rows = await timeProjectionRepo.GetByEmployeeAsync(employeeId, ct);

            var entries = rows.Select(r => new TimeEntry
            {
                EmployeeId = r.EmployeeId,
                Date = r.Date,
                Hours = r.Hours,
                StartTime = r.StartTime,
                EndTime = r.EndTime,
                TaskId = r.TaskId,
                ActivityType = r.ActivityType,
                AgreementCode = r.AgreementCode,
                OkVersion = r.OkVersion,
                RegisteredAt = r.OccurredAt
            }).ToList();

            return Results.Ok(entries);
        }).RequireAuthorization("EmployeeOrAbove")
        // S120 / TASK-12000 — the NAMED SharedKernel model IS the wire shape (the handler
        // serializes TimeEntry instances directly — PAT-012 named-model rule; a BARE ARRAY).
        .Produces<IEnumerable<TimeEntry>>(StatusCodes.Status200OK);

        // ── Absences ──
        //
        // Absence WRITES are owned by the Skema save endpoint (SkemaEndpoints) per
        // ADR-032 D5 (TASK-6606a): the legacy POST /api/absences bypass — which
        // defaulted Hours to a flat 7.4 and carried an advisory OkVersion — was
        // retired so all absence registration flows through Skema consumption
        // valuation. The GET below remains the read surface
        // (WeeklyCalculationPipeline consumes it).

        app.MapGet("/api/absences/{employeeId}", async (
            string employeeId,
            AbsenceProjectionRepository absenceProjectionRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Forbid();

            // For higher roles, verify org scope covers the target employee
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // S27 Phase 4c.6 / TASK-2707: read from absences_projection instead of
            // replaying the event stream. Atomic POST above commits the projection
            // row in the same tx as the outbox enqueue (read-your-write).
            // Full-stream read (no date filter) — uses idx_absences_proj_emp_outbox.
            var rows = await absenceProjectionRepo.GetByEmployeeAsync(employeeId, ct);

            var absences = rows.Select(r => new AbsenceEntry
            {
                EmployeeId = r.EmployeeId,
                Date = r.Date,
                AbsenceType = r.AbsenceType,
                Hours = r.Hours,
                AgreementCode = r.AgreementCode,
                OkVersion = r.OkVersion
            }).ToList();

            return Results.Ok(absences);
        }).RequireAuthorization("EmployeeOrAbove")
        // S120 / TASK-12000 — the NAMED SharedKernel model IS the wire shape (a BARE ARRAY).
        .Produces<IEnumerable<AbsenceEntry>>(StatusCodes.Status200OK);

        // ── Flex Balance ──
        //
        // OUT OF SCOPE for S27 Phase 4c.6 / TASK-2707 (per refinement Assumption #4).
        // FlexBalanceUpdated is NOT yet projected to a read-model table; this handler
        // continues to read from the event stream. Phase 4d / 4e will introduce a
        // flex_balance_projection if read-your-write is required for this endpoint.

        app.MapGet("/api/flex-balance/{employeeId}", async (string employeeId, IEventStore eventStore, OrgScopeValidator scopeValidator, HttpContext context, CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Forbid();

            // For higher roles, verify org scope covers the target employee
            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            // S126 / F5 — one row, not the whole consolidated stream. This handler reads FOUR fields
            // off the event plus a distinct no-history branch, which is why it deserializes through
            // EventSerializer rather than extracting `data->>'…'` per field: the wire shape stays
            // owned by the serializer and the null branch below is unchanged.
            var streamId = $"employee-{employeeId}";
            var latest = await eventStore.ReadLatestOfTypeAsync<FlexBalanceUpdated>(streamId, ct);

            // S120 / TASK-12000 — OWNER RULING #1 (branch-normalization class, 1st instance,
            // ruled 2026-07-21): the no-history branch serves the ONE 5-member shape with the
            // 3 history members null-filled; the vestigial `message` (no reader existed) DIES.
            // The with-history branch below is BYTE-IDENTICAL to the pre-S120 wire.
            if (latest is null)
                return Results.Ok(new FlexBalanceResponse(
                    EmployeeId: employeeId,
                    Balance: 0m,
                    PreviousBalance: null,
                    Delta: null,
                    Reason: null));

            return Results.Ok(new FlexBalanceResponse(
                EmployeeId: employeeId,
                Balance: latest.NewBalance,
                PreviousBalance: latest.PreviousBalance,
                Delta: latest.Delta,
                Reason: latest.Reason));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<FlexBalanceResponse>(StatusCodes.Status200OK); // S120 / TASK-12000

        return app;
    }
}
