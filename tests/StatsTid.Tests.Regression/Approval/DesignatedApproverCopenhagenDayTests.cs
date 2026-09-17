using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
// `StatsTid.Tests.Regression.ReportingLine` is a test NAMESPACE, so the bare model name is ambiguous
// inside this assembly; this is the repo's established alias (see MedarbejderRosterReadTests.cs).
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S142 / TASK-14203 — the DISCRIMINATING proof that approval AUTHORITY is decided on the DANISH
/// calendar day, not on the UTC one. Runs with NO Postgres and NO host (see "Why no container" below),
/// so it is the one fact in this task that can be shown RED on the pre-change code locally.
///
/// <para>
/// <b>Why this matters, in plain language.</b> A "vikar" is a stand-in approver: while a manager is
/// away, someone else may approve their people's timesheets, and the arrangement has a last day
/// (<c>until_date</c>, INCLUSIVE — "til og med"). Denmark is one or two hours ahead of UTC, so between
/// Danish midnight and UTC midnight the UTC calendar still says "yesterday". Before S142 the system
/// asked "is the stand-in still covering?" against the UTC day. So for the ~2 hours after Danish
/// midnight on the day AFTER a vikar expired, the expired stand-in was still granted approval
/// authority — a stale privilege, silently. Access control is an inviolable invariant, so this is the
/// shape worth pinning: the defect FAILS OPEN (it grants, it does not merely deny).
/// </para>
///
/// <para>
/// <b>Why no container is needed.</b> <see cref="DesignatedApproverAuthorizer"/>'s step-3c overloads
/// take an <see cref="IReportingLineDataSource"/> (where the reporting-line and vikar facts come from)
/// and an <see cref="IAuthorityFactsSource"/> (role floor + home Organisation). Supply both and every
/// leg of the predicate runs against in-memory stubs: <c>RoleFloorAsync</c> and the same-Organisation
/// re-check take the <c>facts</c> branch, and the R3 resolution walk
/// (<c>ReportingLineRepository.ResolveDesignatedApproverAsync(source, …)</c>) is pure over the source.
/// The <see cref="NpgsqlConnection"/> argument is structurally required but never opened and never
/// touched on this path. That is what makes this a LOCAL red/green proof rather than a CI-only one.
/// </para>
///
/// <para>
/// <b>Why the stubs are not "testing the test".</b> The stub applies the vikar-coverage rule the
/// interface documents (<c>until_date &gt;= asOf</c>, inclusive) to whatever date it is handed — it does
/// not decide the date. The subject under test is exactly which date the authorizer derives and pushes
/// down its fallback chain, and the test asserts that date as a LITERAL
/// (<c>new DateOnly(2026, 7, 16)</c>), never by calling <c>CopenhagenBusinessDate.Today</c> — a helper
/// compared against itself proves only that it agrees with itself.
/// </para>
/// </summary>
public sealed class DesignatedApproverCopenhagenDayTests
{
    // Census rows 43-45: the authority date is `asOf ?? ctx?.AsOf ?? <derived today>`. These tests
    // drive the TAIL of that chain (asOf null, ctx null), which is the one input that can
    // reintroduce the retired calendar after every caller has been converted.
    private const string Employee = "s142_emp";
    private const string AbsentManager = "s142_absent_mgr";
    private const string StandIn = "s142_stand_in";

    /// <summary>The Organisation both parties share, so the ADR-027 D2 same-Organisation re-check passes.</summary>
    private const string Organisation = "STY02";

    /// <summary>
    /// The stand-in's INCLUSIVE last day. Chosen as the UTC calendar day of
    /// <see cref="BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen"/> (2026-07-15 22:30Z):
    /// at that instant Copenhagen is already 2026-07-16 00:30 (CEST, UTC+02:00), so the vikar is
    /// EXPIRED on the Danish day and still covering on the UTC day. That gap is the whole test.
    /// </summary>
    private static readonly DateOnly VikarLastDay = new(2026, 7, 15);

    /// <summary>
    /// ★ THE RED-ON-PRE-CHANGE FACT. At 2026-07-15 22:30Z it is already 16 July in Copenhagen, so a
    /// vikar whose inclusive last day was the 15th holds NO authority any more and the PRIMARY manager
    /// is the effective approver again.
    ///
    /// <para>
    /// On the pre-change code the fallback derived the UTC day (the 15th), the expired stand-in still
    /// resolved as the effective approver, and this returned <c>true</c> — an over-grant. On the
    /// Copenhagen day (the 16th) it must return <c>false</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ExpiredStandIn_AfterDanishMidnight_HoldsNoApprovalAuthority()
    {
        var lines = new StubReportingLineData(VikarLastDay);
        var facts = new StubAuthorityFacts(Organisation);
        var authorizer = BuildAuthorizer(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);

        var granted = await authorizer.IsEffectiveDesignatedApproverAsync(
            conn: NeverOpenedConnection(), tx: null, ctx: null,
            source: lines, facts: facts,
            actorId: StandIn, employeeId: Employee, asOf: null, ct: default);

        Assert.False(granted);

        // The date the authorizer actually pushed down its own fallback chain, asserted as a LITERAL.
        Assert.Equal(new DateOnly(2026, 7, 16), lines.VikarConsultAsOf);
    }

    /// <summary>
    /// The CONTROL that stops the assertion above from passing for the wrong reason (e.g. a stub that
    /// never returns a vikar at all, or an authorizer that denies everything). Same instant, same
    /// stubs, one input changed: the stand-in's last day is the 16th, so the arrangement IS still
    /// running on the Danish day and authority must be GRANTED.
    /// </summary>
    [Fact]
    public async Task StandInWhoseLastDayIsTheDanishToday_StillHoldsApprovalAuthority()
    {
        var lines = new StubReportingLineData(new DateOnly(2026, 7, 16));
        var facts = new StubAuthorityFacts(Organisation);
        var authorizer = BuildAuthorizer(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);

        var granted = await authorizer.IsEffectiveDesignatedApproverAsync(
            conn: NeverOpenedConnection(), tx: null, ctx: null,
            source: lines, facts: facts,
            actorId: StandIn, employeeId: Employee, asOf: null, ct: default);

        Assert.True(granted);
        Assert.Equal(new DateOnly(2026, 7, 16), lines.VikarConsultAsOf);
    }

    /// <summary>
    /// The WINTER leg. 2026-01-15 23:30Z is already 2026-01-16 00:30 in Copenhagen (CET, UTC+01:00),
    /// so the same over-grant exists outside daylight-saving time and is not a summer-only artefact.
    /// Deliberately paired with the summer case rather than replacing it: the summer instant is the
    /// only one of the two that also kills a hardcoded <c>+01:00</c> implementation
    /// (see <see cref="BoundaryInstants"/>).
    /// </summary>
    [Fact]
    public async Task ExpiredStandIn_AfterDanishMidnightInWinter_HoldsNoApprovalAuthority()
    {
        var lines = new StubReportingLineData(new DateOnly(2026, 1, 15));
        var facts = new StubAuthorityFacts(Organisation);
        var authorizer = BuildAuthorizer(BoundaryInstants.WinterEveningAlreadyTomorrowInCopenhagen);

        var granted = await authorizer.IsEffectiveDesignatedApproverAsync(
            conn: NeverOpenedConnection(), tx: null, ctx: null,
            source: lines, facts: facts,
            actorId: StandIn, employeeId: Employee, asOf: null, ct: default);

        Assert.False(granted);
        Assert.Equal(new DateOnly(2026, 1, 16), lines.VikarConsultAsOf);
    }

    /// <summary>
    /// The NEGATIVE control for a hardcoded <c>+02:00</c> ("summer offset all year") mistake. At
    /// 2026-01-15 22:30Z Copenhagen is still 23:30 on the 15th, so both calendars AGREE; a +02:00
    /// implementation would roll the day over an hour early, expire the vikar a day early and deny.
    /// Authority must be granted, and the date handed down must be the literal 15th.
    /// </summary>
    [Fact]
    public async Task OnTheEveningWhereBothCalendarsAgree_TheAuthorityDateIsThatSharedDay()
    {
        var lines = new StubReportingLineData(new DateOnly(2026, 1, 15));
        var facts = new StubAuthorityFacts(Organisation);
        var authorizer = BuildAuthorizer(BoundaryInstants.WinterEveningCalendarsStillAgree);

        var granted = await authorizer.IsEffectiveDesignatedApproverAsync(
            conn: NeverOpenedConnection(), tx: null, ctx: null,
            source: lines, facts: facts,
            actorId: StandIn, employeeId: Employee, asOf: null, ct: default);

        Assert.True(granted);
        Assert.Equal(new DateOnly(2026, 1, 15), lines.VikarConsultAsOf);
    }

    /// <summary>
    /// The combined edge-or-unit-leader predicate (census row 44) and the unit-leader leg (row 45)
    /// resolve their own fallback tails. This drives the combined entry point so a future edit that
    /// converts only the edge predicate cannot leave the other two on the retired calendar.
    /// </summary>
    [Fact]
    public async Task CombinedEdgeOrUnitLeaderPredicate_AlsoResolvesOnTheDanishDay()
    {
        var lines = new StubReportingLineData(VikarLastDay);
        var facts = new StubAuthorityFacts(Organisation);
        var authorizer = BuildAuthorizer(BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);

        var granted = await authorizer.IsEffectiveApproverOrUnitLeaderAsync(
            conn: NeverOpenedConnection(), tx: null, ctx: null,
            source: lines, facts: facts,
            actorId: StandIn, employeeId: Employee, asOf: null, ct: default);

        Assert.False(granted);
        Assert.Equal(new DateOnly(2026, 7, 16), lines.VikarConsultAsOf);
    }

    // ── Harness ────────────────────────────────────────────────────────────────────────────────

    private static DesignatedApproverAuthorizer BuildAuthorizer(DateTimeOffset pinnedInstant)
    {
        var clock = new FixedTimeProvider(pinnedInstant);
        var factory = new DbConnectionFactory(UnreachableConnectionString);
        return new DesignatedApproverAuthorizer(
            factory,
            new ReportingLineRepository(factory, vikarRepo: null, timeProvider: clock),
            clock);
    }

    /// <summary>
    /// A syntactically valid connection string pointing at nothing. If any leg of the predicate ever
    /// stopped honouring the injected stubs and reached for SQL, the test would FAIL loudly on a
    /// connection error rather than quietly passing — which is the behaviour we want from a seam we
    /// are relying on.
    /// </summary>
    private const string UnreachableConnectionString =
        "Host=statstid-s142-no-database.invalid;Port=5432;Database=none;Username=none;Password=none;Timeout=1";

    private static NpgsqlConnection NeverOpenedConnection() => new(UnreachableConnectionString);

    /// <summary>
    /// The reporting-line/vikar facts, in memory. <see cref="Employee"/> reports PRIMARY to
    /// <see cref="AbsentManager"/> (who is ACTIVE), and that manager owns one approver-owned vikar
    /// naming <see cref="StandIn"/>. Coverage applies the interface's documented rule —
    /// <c>until_date &gt;= asOf</c>, INCLUSIVE — to the date the authorizer hands in, and records that
    /// date so the test can assert it directly.
    /// </summary>
    private sealed class StubReportingLineData : IReportingLineDataSource
    {
        private readonly DateOnly _vikarUntilDate;

        public StubReportingLineData(DateOnly vikarUntilDate) => _vikarUntilDate = vikarUntilDate;

        /// <summary>The <c>asOf</c> the authorizer derived and pushed into the vikar consult.</summary>
        public DateOnly? VikarConsultAsOf { get; private set; }

        public Task<ReportingLineModel?> GetActiveLineAsync(string employeeId, string relationship, CancellationToken ct)
        {
            if (!string.Equals(employeeId, Employee, StringComparison.Ordinal)
                || !string.Equals(relationship, "PRIMARY", StringComparison.Ordinal))
            {
                return Task.FromResult<ReportingLineModel?>(null);
            }

            return Task.FromResult<ReportingLineModel?>(new ReportingLineModel
            {
                ReportingLineId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001"),
                EmployeeId = Employee,
                ManagerId = AbsentManager,
                OrganisationId = Organisation,
                Relationship = "PRIMARY",
                EffectiveFrom = new DateOnly(2020, 1, 1),
                EffectiveTo = null,
                Source = "TEST",
                Version = 1,
                CreatedBy = "test",
            });
        }

        public Task<ManagerVikar?> GetActiveVikarByApproverAsync(string approverId, DateOnly asOf, CancellationToken ct)
        {
            VikarConsultAsOf = asOf;

            if (!string.Equals(approverId, AbsentManager, StringComparison.Ordinal) || _vikarUntilDate < asOf)
                return Task.FromResult<ManagerVikar?>(null);

            return Task.FromResult<ManagerVikar?>(new ManagerVikar
            {
                VikarId = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000002"),
                AbsentApproverId = AbsentManager,
                VikarUserId = StandIn,
                UntilDate = _vikarUntilDate,
                Reason = "S142 boundary fixture",
                OrganisationId = Organisation,
                Version = 1,
                CreatedBy = "test",
                EffectiveTo = null,
            });
        }

        public Task<bool> IsUserActiveAsync(string userId, CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>
    /// The role floor and home-Organisation facts, in memory: everyone clears the LeaderOrAbove floor
    /// and shares one Organisation, so neither gate can mask the date behaviour under test. The
    /// unit-leader leg answers <see cref="UnitLeaderApprovalKind.None"/> so the combined predicate's
    /// result is attributable to the EDGE leg alone.
    /// </summary>
    private sealed class StubAuthorityFacts : IAuthorityFactsSource
    {
        private readonly string _organisation;

        public StubAuthorityFacts(string organisation) => _organisation = organisation;

        public Task<bool> IsActiveLeaderOrAboveAsync(string userId, CancellationToken ct) => Task.FromResult(true);

        public Task<string?> GetActiveHomeOrgAsync(string userId, CancellationToken ct)
            => Task.FromResult<string?>(_organisation);

        public Task<UnitLeaderApprovalKind> GetUnitLeaderKindAsync(string actorId, string employeeId, CancellationToken ct)
            => Task.FromResult(UnitLeaderApprovalKind.None);
    }
}
