using Microsoft.Extensions.Logging;
using StatsTid.Auth;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

/// <summary>
/// Service-layer org-scope validator. Called explicitly from API endpoints
/// to enforce organization-based access control using the actor's RoleScope claims.
///
/// <para>
/// <b>S147 / TASK-14703 (QUAL-230 T5a; R-30) — the departed-self clause.</b> Both employee
/// overloads (<see cref="ValidateEmployeeAccessAsync(ActorContext, string, string?, CancellationToken)"/>
/// and <see cref="ValidateEmployeeAccessIncludingTerminatedAsync(ActorContext, string, string?, CancellationToken)"/>)
/// refuse an actor who IS the target employee when that employee has departed
/// (<see cref="DepartedSubject.IsDeparted(User, DateOnly)"/>: deactivated, or past their last day of
/// employment). The check runs immediately after the target read — the validator already holds the
/// record, so it costs no extra read — and before the terminated-inclusive overload's HR gate. It is the
/// IDENTITY arm only: another person's access is untouched (a manager keeps reaching a leaver during
/// the grace; HR keeps acting on leavers — OQ-2). The Employee own-data branch, the organisation
/// overload and the accessible-organisation read do not run it (the self-service sites carry their own
/// floor). The refusal is the standard <c>(false, reason)</c> shape with the date-free
/// <see cref="DepartedSubject.DenyReason"/>; the WARNING log line names the clause
/// (<see cref="DepartedSubject.ClauseName"/>).
/// </para>
///
/// <para>
/// <b>S147 / TASK-14725 fix-up 1 (owner ruling 2026-11-05, "(a) Extend now") — a target past the
/// manager grace BY DATE counts as switched off.</b> The owner ruled (OQ-2) that a leaver's own manager
/// may act for 5 calendar days after the last day employed and that from D+6 only HR may. Until this
/// fix-up both employee overloads learned that a leaver had passed D+5 only from <c>is_active</c>, which
/// the leaver job's Step A (a 5-minute poll) switches off on D+6 — so between Copenhagen midnight and
/// that poll, or for as long as Step A failed, every route still treated the leaver as an ordinary
/// active employee. Now, after the identity clause, a target whose last day employed lies past the
/// grace (<see cref="EmploymentEndDateLifecycleWriter.IsPastGrace"/>: before today − 5) is answered
/// exactly as a switched-off one, whatever <c>is_active</c> says: the active-only overload answers
/// "Target employee not found" to every actor, and the terminated-inclusive overload admits it only
/// through a scope that is itself LocalHR or above (TASK-14713's rule). D .. D+5, no end date or a
/// future one: unchanged. The switch-off stays as belt and braces. The approval EDGE and the unit-leader
/// path do not pass through this validator, so <see cref="DesignatedApproverAuthorizer"/> carries the
/// same date rule for them (TASK-14725): both layers are needed. The refusal texts are the existing
/// ones (no date, ADR-040 D7); the INFORMATION log line names the clause (<see cref="PastGraceClauseName"/>).
/// </para>
///
/// <para>
/// <b>"Today"</b> is the Copenhagen business day read from the injected <see cref="TimeProvider"/>, at
/// most once per call, and only when a clause needs it: the identity matches (the departed-self clause)
/// or the target has a recorded last day employed (the past-grace rule). The handler that called the
/// validator may read the same provider again for its own dates; the two reads can differ only for a
/// request that straddles Copenhagen midnight (the PAT-028 residual, recorded in the ADR-040 amendment).
/// Every host that builds this validator must therefore register a <see cref="TimeProvider"/> (BC-6: the
/// Backend, Payroll and Orchestrator hosts).
/// </para>
/// </summary>
public sealed class OrgScopeValidator
{
    /// <summary>
    /// S147 / TASK-14725 fix-up 1 — the clause name the INFORMATION log line carries when a target is
    /// refused because it is past the manager grace by date and so treated as switched off (one name
    /// per clause, the S147 house rule). The response body never names it, nor any date.
    /// </summary>
    public const string PastGraceClauseName = "past-grace-as-switched-off";

    // The two existing refusal texts a switched-off target gets, held once so the past-grace answer is
    // provably the same text (S147 / TASK-14725 fix-up 1). Wording unchanged.
    private const string TargetNotFoundReason = "Target employee not found";
    private const string TerminatedRequiresHrReason = "Terminated-employee access requires LocalHR or above";

    private readonly OrganizationRepository _organizationRepository;
    private readonly UserRepository _userRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrgScopeValidator> _logger;

    public OrgScopeValidator(
        OrganizationRepository organizationRepository,
        UserRepository userRepository,
        TimeProvider timeProvider,
        ILogger<OrgScopeValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Validates whether the actor has access to a specific employee's data.
    /// Employee role can only access own data. Higher roles require org-scope coverage.
    ///
    /// <para>
    /// <b>S76 / TASK-7600 B1 per-scope role floor (optional, defaulted off):</b> when
    /// <paramref name="roleFloor"/> is supplied, a scope only admits the target if it ALSO clears
    /// the floor (<see cref="StatsTidRoles.IsAtLeast"/> on <c>scope.Role</c>) — the same per-scope
    /// gate <see cref="ValidateEmployeeAccessIncludingTerminatedAsync"/> applies to terminated
    /// targets. This closes the mixed-role scope leak: an admin-policy endpoint must route through
    /// the floored overload so that a non-admin scope covering org B can NEVER satisfy an admin
    /// gate for B (FAIL-001: this validator binds to ANY covering scope, regardless of that
    /// scope's role). <b>Default <c>null</c> = no floor = byte-identical legacy behavior</b> — the
    /// Employee/Leader (EmployeeOrAbove) callers that rely on any-covering-scope admission are
    /// untouched. The Employee own-data short-circuit is unchanged by the floor (an Employee-only
    /// actor accessing OWN data is not gated by a role floor — ownership, not org-scope, admits).
    /// </para>
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateEmployeeAccessAsync(
        ActorContext actor, string targetEmployeeId, CancellationToken ct = default)
        => await ValidateEmployeeAccessAsync(actor, targetEmployeeId, roleFloor: null, ct);

    /// <summary>
    /// Role-floored overload of <see cref="ValidateEmployeeAccessAsync(ActorContext, string, CancellationToken)"/>.
    /// See that method's S76 / TASK-7600 remarks. Pass <paramref name="roleFloor"/> = the endpoint
    /// policy's minimum admin role (LocalAdmin for LocalAdminOrAbove, LocalHR for HROrAbove); pass
    /// <c>null</c> for the legacy no-floor behavior.
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateEmployeeAccessAsync(
        ActorContext actor, string targetEmployeeId, string? roleFloor, CancellationToken ct = default)
    {
        // Ownership check: Employee accessing own data. The role floor does NOT apply to the
        // own-data branch — ownership (not org-scope) is what admits here, and an actor reading
        // their OWN record is never a privilege escalation. (No admin-policy caller routes here:
        // admin endpoints carry an HROrAbove/LocalAdminOrAbove policy that excludes Employee
        // tokens, so this branch is reached only by EmployeeOrAbove callers that pass roleFloor
        // = null anyway.)
        if (IsEmployeeOnly(actor))
        {
            if (string.Equals(actor.ActorId, targetEmployeeId, StringComparison.Ordinal))
                return (true, null);

            return Deny(actor, targetEmployeeId, "Employee can only access own data");
        }

        // Higher roles: resolve target employee's org and check scope coverage
        if (actor.Scopes is null || actor.Scopes.Length == 0)
            return Deny(actor, targetEmployeeId, "No scopes assigned");

        var targetUser = await _userRepository.GetByIdAsync(targetEmployeeId, ct);
        if (targetUser is null)
            return Deny(actor, targetEmployeeId, TargetNotFoundReason);

        // PAT-028 — this call's ONE Copenhagen business date, read lazily and at most once (only when
        // a clause below needs it: the identity matches, or the target has a recorded last day).
        DateOnly? today = null;
        DateOnly Today() => today ??= CopenhagenBusinessDate.Today(_timeProvider);

        // S147 / TASK-14703 (R-30) — the departed-self clause, right after the target read. On this
        // active-only overload a DEACTIVATED self has already answered "Target employee not found"
        // above (as before); what this adds is the self still active but past their last day (the
        // grace between the last day and the deactivation). Identity arm only — see the class remarks.
        if (IsDepartedSelf(actor, targetEmployeeId, targetUser, Today))
            return DenyDepartedSelf(actor, targetEmployeeId);

        // S147 / TASK-14725 fix-up 1 (owner ruling 2026-11-05, "(a) Extend now") — a target past the
        // manager grace BY DATE is answered exactly as a switched-off target is answered by the
        // active-only read above: "Target employee not found", to every actor. The read above only
        // stops matching once the leaver job's Step A has run; this makes the answer the same from
        // Copenhagen midnight on D+6, and for as long as Step A fails. Runs after the identity clause,
        // so a departed self keeps that clause's answer (unchanged). See the class remarks.
        if (IsPastGraceByDate(targetUser, Today))
            return DenyPastGraceAsSwitchedOff(actor, targetEmployeeId, TargetNotFoundReason);

        var targetOrg = await _organizationRepository.GetByIdAsync(targetUser.PrimaryOrgId, ct);
        if (targetOrg is null)
            return Deny(actor, targetEmployeeId, "Target organization not found");

        // Cache for scope org path lookups within this request
        var orgPathCache = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var scope in actor.Scopes)
        {
            // S76 B1 per-scope floor: a scope below the policy floor never admits (continue —
            // the final Deny fires if no scope qualifies). Inert when roleFloor is null.
            if (roleFloor is not null && !StatsTidRoles.IsAtLeast(scope.Role, roleFloor))
                continue;

            if (scope.ScopeType == "GLOBAL")
                return (true, null);

            if (scope.OrgId is null)
                continue;

            if (!orgPathCache.TryGetValue(scope.OrgId, out var scopeOrgPath))
            {
                var scopeOrg = await _organizationRepository.GetByIdAsync(scope.OrgId, ct);
                scopeOrgPath = scopeOrg?.MaterializedPath;
                orgPathCache[scope.OrgId] = scopeOrgPath;
            }

            if (scope.CoversOrg(targetOrg.MaterializedPath, scopeOrgPath))
                return (true, null);
        }

        return Deny(actor, targetEmployeeId, "Actor scope does not cover target organization");
    }

    /// <summary>
    /// S70 / TASK-7003 (SPRINT-70 R9b, ADR-033 slice 3a) — terminated-INCLUSIVE employee-access
    /// validation for the R9c allowlist surfaces ONLY (the employment-end-date set/clear endpoint,
    /// the settlement manual-resolve endpoint, the reconcile-payout endpoint, and the year-overview
    /// read; since S136 / TASK-13603 the two REGISTRATION WRITERS (time-entries POST, Skema save) and
    /// since S138 / TASK-13805 the approval SEND command, and since S138 / TASK-13802 the employee-profile GET + PUT +
    /// the dedicated agreement-code endpoint — each with the ADR-040 D3 role floor as
    /// <c>roleFloor</c>, so a deactivated subject is addressable ONLY by an admitting scope that is
    /// itself LocalHR+; the general read paths stay active-only).
    /// <b>S147 / TASK-14713 (QUAL-230 T5b; owner OQ-2: HR only from the sixth day after a leaver's last
    /// working day)</b> adds the surfaces that FINISH a switched-off leaver's final month: approve, reject
    /// and the leader arm of reopen (<c>roleFloor</c> LocalHR; the designated-approver edge beside them
    /// refuses the leaver's manager from that day BY DATE — S147 / TASK-14725, the
    /// <c>DesignatedApproverAuthorizer</c> manager-grace check — and still fails closed for a deactivated
    /// subject as belt and braces; since TASK-14725 fix-up 1 this overload, too, treats a subject past
    /// the grace by date as switched off, so "from that day" no longer waits for Step A on either
    /// layer); the Skema month view's HR tier, through <c>ApprovalReadTier.ClassifyMonthViewTierIncludingTerminatedAsync</c>
    /// (that view only; the shared <c>IsLeaderTierReadAsync</c> stays on the active-only overload); and the
    /// Payroll host's <c>/calculate-and-export</c> (<c>roleFloor</c> LocalAdmin). Each is bounded AFTER this
    /// gate to a month that holds an employed day (ADR-040 D3's date-free refusal), and the three decision
    /// endpoints take the terminated-inclusive tree-lock overload
    /// (<c>ReportingLineRepository.AcquireTreeLockForEmployeeIncludingTerminatedAsync</c>) and read the audit's
    /// target organisation terminated-inclusive. On all of them the departed-self identity clause below runs
    /// first, so nobody reaches their OWN departed record through this widening.
    /// <b>S147 / TASK-14703 (R-30, R-34) — the departed-self clause</b> runs on this surface (and on
    /// <see cref="ValidateEmployeeAccessAsync(ActorContext, string, string?, CancellationToken)"/>)
    /// right after the target read and BEFORE the HR gate: a terminated-inclusive read never lets an
    /// actor act on their OWN departed record, whatever their role (see the class remarks). The
    /// inline routes' deny-only terminated-inclusive reads of the actor's own record, which only ever
    /// refuse, are inventoried on <see cref="DepartedSubject"/>.
    /// Resolves the target via
    /// <see cref="UserRepository.GetByIdIncludingTerminatedAsync(string, CancellationToken)"/> so a
    /// deactivated leaver IS addressable (the S68 B2 fix), then applies the EXISTING
    /// <see cref="RoleScope.CoversOrg"/>/<c>PrimaryOrgId</c> subtree check unchanged. The shared
    /// <see cref="ValidateEmployeeAccessAsync"/> (and its active-only target resolution) is
    /// untouched — every non-allowlisted caller still cannot see a terminated employee.
    ///
    /// <para>
    /// <b>NO own-data branch exists on this surface (decision, R9b).</b> The Employee own-data
    /// short-circuit is deliberately ABSENT: an Employee-only actor is denied outright, never
    /// granted by ownership. Rationale: (1) without the role gate first, an Employee's ORG_ONLY
    /// scope could satisfy the subtree loop for a same-org COLLEAGUE — the existing validator
    /// prevents that exact privilege escalation via its own-data-only branch, and this validator
    /// must be at least as strict; (2) the R9e matrix pins terminated-SELF behavior on the
    /// EXISTING validator (own-data branch passes, the endpoint then 404s on the filtered
    /// <c>GetByIdAsync</c>), not on this one. On the allowlisted write surfaces the
    /// <c>HROrAbove</c> endpoint policy already excludes Employee tokens; this branch is the
    /// defense-in-depth for any future read surface wired here.
    /// </para>
    ///
    /// <para>
    /// <b>HROrAbove gate (R9b), enforced at the moment it matters:</b> when the resolved target is
    /// TERMINATED (<c>is_active = FALSE</c>; since S147 / TASK-14725 fix-up 1 also a target past the
    /// manager grace by date — see the class remarks), the actor's role must be LocalHR or above
    /// (<see cref="StatsTidRoles.IsAtLeast"/> vs <see cref="StatsTidRoles.LocalHR"/> — the same
    /// GlobalAdmin/LocalAdmin/LocalHR set the <c>HROrAbove</c> endpoint policy admits). For an
    /// ACTIVE target this validator is deliberately behavior-identical to
    /// <see cref="ValidateEmployeeAccessAsync"/>'s non-Employee path (scope subtree only, no HR
    /// floor): the year-overview surface is <c>EmployeeOrAbove</c>-policy and an in-scope
    /// LocalLeader's access to an ACTIVE subordinate is pre-existing, pinned behavior
    /// (<c>YearOverviewTests.Auth_LeaderInScope_Returns200</c>) that R9c's wiring must not
    /// regress — the HR floor R9b adds is the gate on TERMINATED-employee data, which is the only
    /// data this validator unlocks beyond the existing one. On the resolve/reconcile-payout
    /// surfaces the endpoint policy enforces HROrAbove for ALL targets regardless.
    /// </para>
    ///
    /// <para>
    /// <b>R9f1 per-scope floor (Step-5a hardening, Codex B1, 2026-06-10):</b> the R9b
    /// primary-role gate alone is spoofable by a MIXED-role JWT — an actor holding LocalHR in a
    /// DISJOINT org plus a LocalLeader scope covering the target carries primary role LocalHR,
    /// passes the gate, and would then be admitted by the Leader scope. So for a TERMINATED
    /// target the scope that ADMITS must ITSELF be LocalHR or above
    /// (<see cref="StatsTidRoles.IsAtLeast"/> on <c>scope.Role</c>), in BOTH the GLOBAL branch
    /// and the <see cref="RoleScope.CoversOrg"/> branch. A below-floor scope simply never admits
    /// a terminated target (the loop continues; if no scope qualifies, the final subtree Deny
    /// fires). The early primary-role gate is KEPT as defense-in-depth/fail-fast. For an ACTIVE
    /// target the floor is inert — behavior remains identical to
    /// <see cref="ValidateEmployeeAccessAsync"/>'s non-Employee path (any covering scope, no
    /// role floor).
    /// </para>
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateEmployeeAccessIncludingTerminatedAsync(
        ActorContext actor, string targetEmployeeId, CancellationToken ct = default)
        => await ValidateEmployeeAccessIncludingTerminatedAsync(actor, targetEmployeeId, roleFloor: null, ct);

    /// <summary>
    /// Role-floored overload of
    /// <see cref="ValidateEmployeeAccessIncludingTerminatedAsync(ActorContext, string, CancellationToken)"/>.
    ///
    /// <para>
    /// <b>S76 / TASK-7600 B1 fix-forward cycle 2 (Codex Step-5a c2, MUST-FIX) — the ACTIVE-target
    /// leak.</b> The R9f1 per-scope floor on the loop above was conditioned on
    /// <c>terminatedTarget</c>, so it ONLY bit a TERMINATED target. For an ACTIVE target the loop
    /// admitted via ANY covering scope — letting a mixed-role JWT (HR@A + Leader@B) perform HR
    /// WRITES on an ACTIVE employee in B through the Leader scope, across every HROrAbove caller
    /// that routes here. The fix lifts the floor onto an explicit <paramref name="roleFloor"/>
    /// parameter applied to <b>BOTH</b> the ACTIVE and the TERMINATED scope loops: when a non-null
    /// floor is supplied, a scope only admits (in either the GLOBAL branch or the
    /// <see cref="RoleScope.CoversOrg"/> branch) if it ALSO clears the floor
    /// (<see cref="StatsTidRoles.IsAtLeast"/> on <c>scope.Role</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Terminated behavior is unchanged when a floor is passed</b> — the R9f1 invariant (the
    /// ADMITTING scope must itself be LocalHR or above for a terminated target) still holds, now
    /// expressed via <paramref name="roleFloor"/> rather than a hard-coded LocalHR. Every admin
    /// HROrAbove caller passes <c>StatsTidRoles.LocalHR</c>, which equals the prior hard-coded
    /// floor for terminated targets and ADDS the same floor for active targets. <b>Default
    /// <c>null</c></b> keeps the legacy any-covering-scope-for-active / hard-LocalHR-for-terminated
    /// behavior — but no production caller passes null: the leader year-overview READS that must
    /// admit an in-scope leader for an ACTIVE report route through the no-floor 3-arg overload
    /// (<c>BalanceEndpoints</c> :633/:1224, pinned by
    /// <c>YearOverviewTests.Auth_LeaderInScope_Returns200</c>), where the floor is null AND the
    /// terminated branch still hard-floors at LocalHR (defense-in-depth below).
    /// </para>
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateEmployeeAccessIncludingTerminatedAsync(
        ActorContext actor, string targetEmployeeId, string? roleFloor, CancellationToken ct = default)
    {
        // No own-data branch (see XML doc): Employee-only actors are denied outright.
        if (IsEmployeeOnly(actor))
            return Deny(actor, targetEmployeeId, "Employee role cannot use the terminated-inclusive access surface");

        if (actor.Scopes is null || actor.Scopes.Length == 0)
            return Deny(actor, targetEmployeeId, "No scopes assigned");

        // Terminated-INCLUSIVE target resolution — the single difference from the shared
        // validator's read (which filters is_active = TRUE and turns a leaver into
        // "Target employee not found").
        var targetUser = await _userRepository.GetByIdIncludingTerminatedAsync(targetEmployeeId, ct);
        if (targetUser is null)
            return Deny(actor, targetEmployeeId, TargetNotFoundReason);

        // PAT-028 — this call's ONE Copenhagen business date, read lazily and at most once.
        DateOnly? today = null;
        DateOnly Today() => today ??= CopenhagenBusinessDate.Today(_timeProvider);

        // S147 / TASK-14703 (R-30) — the departed-self clause, right after the target read and BEFORE
        // the HR gate below. On this overload a deactivated self IS resolved, and before S147 an HR-tier
        // self passed the gate (the S136 adjudicated residual, re-decided by the owner's OQ-2): the
        // clause refuses both the deactivated self and the self past their last day. Identity arm only —
        // HR acting on someone ELSE who has left still reaches the gate and the scope loop as before.
        if (IsDepartedSelf(actor, targetEmployeeId, targetUser, Today))
            return DenyDepartedSelf(actor, targetEmployeeId);

        // S147 / TASK-14725 fix-up 1 (owner ruling 2026-11-05, "(a) Extend now") — "switched off" for
        // THIS overload's two HR gates below means: deactivated (is_active = FALSE), OR past the
        // manager grace BY DATE (EmploymentEndDateLifecycleWriter.IsPastGrace — last day employed
        // before today − 5, so from D+6) whatever is_active says. A past-grace target is therefore
        // admitted only through a scope that is itself LocalHR or above, exactly as a deactivated one
        // (TASK-14713's rule) — from Copenhagen midnight on D+6, not from Step A's poll. D .. D+5, no
        // end date, or a future one: unchanged. The departed-self clause above is unchanged.
        var pastGraceWhileActive = targetUser.IsActive && IsPastGraceByDate(targetUser, Today);
        var terminatedTarget = !targetUser.IsActive || pastGraceWhileActive;

        // R9b HROrAbove gate on terminated-employee data (fail-closed BEFORE the scope loop, so
        // a non-HR actor holding a GLOBAL scope is still refused a terminated target here).
        // Kept as defense-in-depth/fail-fast — the per-scope R9f1 floor inside the loop is the
        // gate that actually binds privilege to the ADMITTING scope.
        if (terminatedTarget &&
            (actor.ActorRole is null || !StatsTidRoles.IsAtLeast(actor.ActorRole, StatsTidRoles.LocalHR)))
        {
            return pastGraceWhileActive
                ? DenyPastGraceAsSwitchedOff(actor, targetEmployeeId, TerminatedRequiresHrReason)
                : Deny(actor, targetEmployeeId, TerminatedRequiresHrReason);
        }

        var targetOrg = await _organizationRepository.GetByIdAsync(targetUser.PrimaryOrgId, ct);
        if (targetOrg is null)
            return Deny(actor, targetEmployeeId, "Target organization not found");

        // CoversOrg/PrimaryOrgId subtree check — identical to ValidateEmployeeAccessAsync for an
        // ACTIVE target; for a TERMINATED target (S147 / TASK-14725: or one past the grace by date)
        // each scope must ALSO clear the R9f1 per-scope HROrAbove floor below before it can admit.
        var orgPathCache = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var scope in actor.Scopes)
        {
            // R9f1 per-scope floor (Step-5a hardening, Codex B1): for a TERMINATED target the
            // scope that ADMITS must itself be LocalHR or above — the R9b primary-role gate
            // above is necessary but NOT sufficient (a mixed-role JWT with LocalHR in a disjoint
            // org + a LocalLeader scope covering the target carries primary role LocalHR yet
            // would be admitted here by the Leader scope). Applies to BOTH the GLOBAL branch and
            // the CoversOrg branch; a below-floor scope simply never admits a terminated target
            // (continue — the final Deny fires if no scope qualifies). Kept as a hard LocalHR
            // floor even when roleFloor is null (fail-closed defense-in-depth: the no-floor
            // leader year-overview READS only ever touch ACTIVE targets — a terminated target
            // never reaches an admitting scope without LocalHR).
            if (terminatedTarget && !StatsTidRoles.IsAtLeast(scope.Role, StatsTidRoles.LocalHR))
                continue;

            // S76 B1 fix-forward (cycle 2, Codex c2 MUST-FIX): the EXPLICIT per-scope floor —
            // applied to BOTH ACTIVE and TERMINATED targets — closes the active-target leak. With
            // a non-null roleFloor (every admin HROrAbove caller passes StatsTidRoles.LocalHR), a
            // scope below the floor never admits even an ACTIVE target, so a mixed HR@A + Leader@B
            // JWT can no longer write/read an active B employee through the Leader scope. Inert
            // when roleFloor is null (the leader year-overview reads).
            if (roleFloor is not null && !StatsTidRoles.IsAtLeast(scope.Role, roleFloor))
                continue;

            if (scope.ScopeType == "GLOBAL")
                return (true, null);

            if (scope.OrgId is null)
                continue;

            if (!orgPathCache.TryGetValue(scope.OrgId, out var scopeOrgPath))
            {
                var scopeOrg = await _organizationRepository.GetByIdAsync(scope.OrgId, ct);
                scopeOrgPath = scopeOrg?.MaterializedPath;
                orgPathCache[scope.OrgId] = scopeOrgPath;
            }

            if (scope.CoversOrg(targetOrg.MaterializedPath, scopeOrgPath))
                return (true, null);
        }

        return Deny(actor, targetEmployeeId, "Actor scope does not cover target organization");
    }

    /// <summary>
    /// Validates whether the actor has access to a specific organization.
    /// Checks actor scopes against the target organization's materialized path.
    ///
    /// <para>
    /// <b>S76 / TASK-7600 B1 per-scope role floor (optional, defaulted off):</b> see
    /// <see cref="ValidateEmployeeAccessAsync(ActorContext, string, string?, CancellationToken)"/>.
    /// When <paramref name="roleFloor"/> is supplied, a scope only admits the target org if it ALSO
    /// clears the floor — closing the mixed-role leak on admin-policy org gates (a non-admin scope
    /// covering org B can no longer satisfy an admin gate for B). Default <c>null</c> = no floor =
    /// byte-identical legacy behavior for the EmployeeOrAbove/LeaderOrAbove callers.
    /// </para>
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateOrgAccessAsync(
        ActorContext actor, string targetOrgId, CancellationToken ct = default)
        => await ValidateOrgAccessAsync(actor, targetOrgId, roleFloor: null, ct);

    /// <summary>
    /// Role-floored overload of <see cref="ValidateOrgAccessAsync(ActorContext, string, CancellationToken)"/>.
    /// Pass <paramref name="roleFloor"/> = the endpoint policy's minimum admin role; <c>null</c> for
    /// the legacy no-floor behavior.
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateOrgAccessAsync(
        ActorContext actor, string targetOrgId, string? roleFloor, CancellationToken ct = default)
    {
        var targetOrg = await _organizationRepository.GetByIdAsync(targetOrgId, ct);
        if (targetOrg is null)
            return Deny(actor, targetOrgId, "Organization not found");

        if (actor.Scopes is null || actor.Scopes.Length == 0)
            return Deny(actor, targetOrgId, "No scopes assigned");

        // Cache for scope org path lookups within this request
        var orgPathCache = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var scope in actor.Scopes)
        {
            // S76 B1 per-scope floor: a scope below the policy floor never admits (continue).
            // Inert when roleFloor is null.
            if (roleFloor is not null && !StatsTidRoles.IsAtLeast(scope.Role, roleFloor))
                continue;

            if (scope.ScopeType == "GLOBAL")
                return (true, null);

            if (scope.OrgId is null)
                continue;

            if (!orgPathCache.TryGetValue(scope.OrgId, out var scopeOrgPath))
            {
                var scopeOrg = await _organizationRepository.GetByIdAsync(scope.OrgId, ct);
                scopeOrgPath = scopeOrg?.MaterializedPath;
                orgPathCache[scope.OrgId] = scopeOrgPath;
            }

            if (scope.CoversOrg(targetOrg.MaterializedPath, scopeOrgPath))
                return (true, null);
        }

        return Deny(actor, targetOrgId, "Actor scope does not cover target organization");
    }

    /// <summary>
    /// S44 / TASK-4405 — ADR-026 D5 audit-visibility surface read-path scope resolution.
    /// Returns the set of org_ids the actor can access for scope-by-target queries
    /// against <c>audit_projection</c>.
    ///
    /// <para>Per-role contract:</para>
    /// <list type="bullet">
    ///   <item><description><b>GlobalAdmin</b> (any scope with <c>ScopeType == "GLOBAL"</c>) →
    ///   returns <c>null</c> sentinel meaning "no filter" — caller treats as unrestricted.
    ///   Used by <c>AuditProjectionRepository.QueryByOrgScopeAsync</c> to include
    ///   <c>visibility_scope = 'GLOBAL_ADMIN_ONLY'</c> rows.</description></item>
    ///   <item><description><b>LocalAdmin / Manager</b> (scopes with non-null
    ///   <c>OrgId</c>) → returns the union of materialized-path descendants of each
    ///   scope org. Allows scope-by-target queries to filter
    ///   <c>target_org_id = ANY(@accessibleOrgIds)</c>.</description></item>
    ///   <item><description><b>Employee / no scopes</b> → returns empty list. Caller
    ///   treats as 403 at endpoint layer.</description></item>
    /// </list>
    ///
    /// <para>Signature takes full <see cref="ActorContext"/> per S44 Step 4 cycle 1
    /// Reviewer W3 absorption (matches the sibling <see cref="ValidateEmployeeAccessAsync"/>
    /// + <see cref="ValidateOrgAccessAsync"/> shape; both read role + scopes).</para>
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetAccessibleOrgsAsync(
        ActorContext actor, CancellationToken ct = default)
        => await GetAccessibleOrgsAsync(actor, roleFloor: null, ct);

    /// <summary>
    /// Role-floored overload of <see cref="GetAccessibleOrgsAsync(ActorContext, CancellationToken)"/>.
    ///
    /// <para>
    /// <b>S76 / TASK-7600 B1 per-scope role floor:</b> when <paramref name="roleFloor"/> is
    /// supplied, only scopes that clear the floor (<see cref="StatsTidRoles.IsAtLeast"/> on
    /// <c>scope.Role</c>) contribute to the accessible-org union, and the GlobalAdmin short-circuit
    /// fires only for a GLOBAL scope that ITSELF clears the floor. This closes the picker leak: the
    /// person-search picker (admin surface) must route through the floored overload so a mixed-role
    /// actor's non-admin scope covering org B cannot widen the accessible-org set into B's roster.
    /// Default <c>null</c> = byte-identical legacy union (the audit-visibility / settlement reads
    /// that intentionally union over every covering scope are untouched).
    /// </para>
    /// </summary>
    public Task<IReadOnlyList<string>?> GetAccessibleOrgsAsync(
        ActorContext actor, string? roleFloor, CancellationToken ct = default)
    {
        if (actor.Scopes is null || actor.Scopes.Length == 0)
            return Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());

        // GlobalAdmin short-circuit — any GLOBAL scope means "see everything". Under a floor the
        // GLOBAL scope must itself clear it (a below-floor GLOBAL scope cannot grant unrestricted
        // admin reach).
        if (actor.Scopes.Any(s =>
                string.Equals(s.ScopeType, "GLOBAL", StringComparison.Ordinal) &&
                (roleFloor is null || StatsTidRoles.IsAtLeast(s.Role, roleFloor))))
            return Task.FromResult<IReadOnlyList<string>?>(null);

        // Non-global: the accessible-org set is EXACTLY the assigned (role-floored) ORG_ONLY orgs.
        // S93 / ADR-035 slice 2 (flat role-scope): no materialized-path subtree expansion —
        // coverage is exact Organisation-set membership (the union of the actor's ORG_ONLY rows).
        // No DB round-trip is needed anymore (the GetDescendantsAsync call is gone), so this is a
        // synchronous projection returned via a completed Task.
        var accessibleOrgIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in actor.Scopes)
        {
            // S76 B1 per-scope floor: a scope below the floor contributes nothing to the union.
            // Inert when roleFloor is null.
            if (roleFloor is not null && !StatsTidRoles.IsAtLeast(scope.Role, roleFloor))
                continue;

            // S93 hardening (ADR-035 slice 2, Step-7a): only ORG_ONLY scopes contribute. DEFAULT-DENY
            // any non-ORG_ONLY non-GLOBAL type (GLOBAL is already handled by the short-circuit above
            // and carries a null OrgId) — a stale pre-S93 token's removed type (ORG_AND_DESCENDANTS)
            // must not inject its org into the accessible set.
            if (!string.Equals(scope.ScopeType, "ORG_ONLY", StringComparison.Ordinal))
                continue;

            if (scope.OrgId is null)
                continue;

            accessibleOrgIds.Add(scope.OrgId);
        }

        return Task.FromResult<IReadOnlyList<string>?>(accessibleOrgIds.ToArray());
    }

    /// <summary>
    /// Returns true if the actor only holds the Employee role (no higher-privilege scopes).
    /// </summary>
    private static bool IsEmployeeOnly(ActorContext actor)
    {
        // Check explicit role first
        if (string.Equals(actor.ActorRole, StatsTidRoles.Employee, StringComparison.Ordinal))
            return true;

        // If scopes are present, check if all scopes are Employee-level
        if (actor.Scopes is { Length: > 0 })
            return actor.Scopes.All(s =>
                string.Equals(s.Role, StatsTidRoles.Employee, StringComparison.Ordinal));

        // No role, no scopes — treat as Employee (least privilege)
        return actor.ActorRole is null;
    }

    /// <summary>
    /// S147 / TASK-14703 (R-30) — the clause's test: the actor IS the target (Ordinal; a null or empty
    /// actor id never matches) AND the target has departed as of the Copenhagen business day. "Today"
    /// comes from the caller's per-call lazy date (<paramref name="today"/>) and is asked for only when
    /// the identity matches.
    /// </summary>
    private static bool IsDepartedSelf(
        ActorContext actor, string targetEmployeeId, User targetUser, Func<DateOnly> today) =>
        DepartedSubject.IsSelf(actor.ActorId, targetEmployeeId)
        && DepartedSubject.IsDeparted(targetUser, today());

    /// <summary>
    /// S147 / TASK-14725 fix-up 1 — the target's last day employed is recorded and lies past the owner's
    /// 5-day manager grace at the call's Copenhagen date
    /// (<see cref="EmploymentEndDateLifecycleWriter.IsPastGrace"/>: false on D .. D+5, true from D+6),
    /// whatever <c>is_active</c> says. The same helper the leaver job's Step A and
    /// <see cref="DesignatedApproverAuthorizer"/> decide through, so the grace length has one home.
    /// "Today" is asked for only when an end date is recorded.
    /// </summary>
    private static bool IsPastGraceByDate(User targetUser, Func<DateOnly> today) =>
        targetUser.EmploymentEndDate is { } endDate
        && EmploymentEndDateLifecycleWriter.IsPastGrace(endDate, today());

    /// <summary>
    /// S147 / TASK-14725 fix-up 1 — the refusal for a target treated as switched off because it is past
    /// the grace by date: the SAME <c>(false, reason)</c> a deactivated target gets on that overload
    /// (<paramref name="reason"/> is that overload's existing text — no date, ADR-040 D7), and ONE
    /// INFORMATION line that names the clause (<see cref="PastGraceClauseName"/>) so the refusal is
    /// traceable to this rule rather than to the deactivation.
    /// <para>
    /// Information, not Warning (TASK-14725 fix-up 2; Step 7a cycle 2, N-4): the refusal is the rule
    /// working as designed, not a fault or a probe. Until the leaver job switches the subject off, every
    /// refused request for a past-grace, still-active subject lands here (on both overloads; on the
    /// active-only routes HR's too), so a Warning would fill the log with expected refusals. The level
    /// matches the approver predicate's line for the same rule on the other layer
    /// (<see cref="DesignatedApproverAuthorizer.ManagerGraceEndedClause"/>). Deliberately not throttled:
    /// one line per refusal keeps each refusal traceable.
    /// </para>
    /// </summary>
    private (bool Allowed, string? Reason) DenyPastGraceAsSwitchedOff(ActorContext actor, string target, string reason)
    {
        _logger.LogInformation(
            "Access denied ({Clause}): Actor={ActorId} Role={ActorRole} Target={Target} Reason={Reason} CorrelationId={CorrelationId}",
            PastGraceClauseName, actor.ActorId, actor.ActorRole, target, reason, actor.CorrelationId);
        return (false, reason);
    }

    /// <summary>
    /// The clause's refusal: the standard <c>(false, reason)</c> shape with the date-free
    /// <see cref="DepartedSubject.DenyReason"/>, and ONE WARNING line that names the clause
    /// (<see cref="DepartedSubject.ClauseName"/>) so the refusal is traceable to this rule.
    /// </summary>
    private (bool Allowed, string? Reason) DenyDepartedSelf(ActorContext actor, string target)
    {
        _logger.LogWarning(
            "Access denied ({Clause}): Actor={ActorId} Role={ActorRole} Target={Target} Reason={Reason} CorrelationId={CorrelationId}",
            DepartedSubject.ClauseName, actor.ActorId, actor.ActorRole, target, DepartedSubject.DenyReason, actor.CorrelationId);
        return (false, DepartedSubject.DenyReason);
    }

    private (bool Allowed, string? Reason) Deny(ActorContext actor, string target, string reason)
    {
        _logger.LogWarning(
            "Access denied: Actor={ActorId} Role={ActorRole} Target={Target} Reason={Reason} CorrelationId={CorrelationId}",
            actor.ActorId, actor.ActorRole, target, reason, actor.CorrelationId);
        return (false, reason);
    }
}
