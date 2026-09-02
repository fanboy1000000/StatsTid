using System.Globalization;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;

namespace StatsTid.Tests.Regression.Infrastructure;

/// <summary>
/// S136 / TASK-13602 — contract tests for <see cref="EmploymentWindowResolver"/>
/// (ADR-040 D1/D2/D3), exercised through BOTH of its public surfaces: the SharedKernel
/// self-managed <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentWindowResolver"/>
/// and the Infrastructure in-tx <see cref="IEmploymentWindowResolverInTx"/> (the
/// IOutboxEnqueue split-interface pattern — one concrete, two contracts). Pins, for BOTH:
///
/// <list type="bullet">
///   <item><description>D1 boundary matrix — end date INCLUSIVE (the last day employed):
///   day-before-start ⇒ NOT_EMPLOYED, == start ⇒ EMPLOYED, == end ⇒ EMPLOYED,
///   day-after-end ⇒ NOT_EMPLOYED.</description></item>
///   <item><description>D2 — NULL start / NULL end / both-NULL are unbounded on the NULL
///   side (why enforcement needs no data backfill).</description></item>
///   <item><description>D3 — the window is a date fact independent of <c>is_active</c>:
///   a deactivated leaver's in-window dates still resolve EMPLOYED (the
///   leaver-correction flow an is_active copy-paste filter would silently break).</description></item>
///   <item><description>Missing users row ⇒ <see cref="InvalidOperationException"/>
///   (fail-loud data assertion, never a NOT_EMPLOYED answer).</description></item>
///   <item><description>The in-tx surface actually rides the caller's transaction —
///   it sees an UNCOMMITTED window edit that the self-managed surface cannot. This is
///   the two-surface rationale made falsifiable: a "harmonized" self-managed-only
///   implementation fails this test.</description></item>
/// </list>
///
/// <para>
/// Located in <c>StatsTid.Tests.Regression</c> (not <c>StatsTid.Tests.Unit</c>) because
/// the boundary logic is exercised end-to-end through the real <c>users</c> schema and
/// Unit lacks the Npgsql + Testcontainers references — the same placement rationale as
/// <see cref="TxContractTests"/> and the EmploymentProfileResolver suites
/// (EmployeeProfileMarqueeTests et al.), which are all Docker-gated regression tests.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentWindowResolverTests : IAsyncLifetime
{
    // Window shapes seeded once in InitializeAsync. Org STY01 is seeded by init.sql.
    private const string BoundedEmployee = "EMP-WIN-BOUNDED";      // [2026-03-10, 2026-06-20]
    private const string NullStartEmployee = "EMP-WIN-NULLSTART";  // [NULL, 2026-06-20]
    private const string NullEndEmployee = "EMP-WIN-NULLEND";      // [2026-03-10, NULL]
    private const string OpenEmployee = "EMP-WIN-OPEN";            // [NULL, NULL]
    private const string LeaverEmployee = "EMP-WIN-LEAVER";        // bounded + is_active=FALSE
    private const string TxProbeEmployee = "EMP-WIN-TXPROBE";      // [NULL, NULL], mutated in-tx only

    private static readonly DateOnly Start = new(2026, 3, 10);
    private static readonly DateOnly End = new(2026, 6, 20);

    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    // One concrete, addressed through its two public contracts (dual-binding parity
    // with Program.cs — a regression that drops either interface breaks compilation here).
    private StatsTid.SharedKernel.Interfaces.IEmploymentWindowResolver _selfManaged = null!;
    private IEmploymentWindowResolverInTx _inTx = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        // Full init.sql so the canonical users table (employment_start_date /
        // employment_end_date columns) exists — the harness's own DDL is the
        // 4-table segmentation subset only.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        var resolver = new EmploymentWindowResolver(_harness.Factory);
        _selfManaged = resolver;
        _inTx = resolver;

        await SeedUserAsync(BoundedEmployee, Start, End);
        await SeedUserAsync(NullStartEmployee, null, End);
        await SeedUserAsync(NullEndEmployee, Start, null);
        await SeedUserAsync(OpenEmployee, null, null);
        await SeedUserAsync(LeaverEmployee, Start, End, isActive: false, endDateDeactivated: true);
        await SeedUserAsync(TxProbeEmployee, null, null);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // D1 — boundary matrix on a bounded spell (end INCLUSIVE)
    // ═════════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData("2026-03-09", EmploymentWindowStatus.NOT_EMPLOYED, false)] // day before start
    [InlineData("2026-03-09", EmploymentWindowStatus.NOT_EMPLOYED, true)]
    [InlineData("2026-03-10", EmploymentWindowStatus.EMPLOYED, false)]     // == start
    [InlineData("2026-03-10", EmploymentWindowStatus.EMPLOYED, true)]
    [InlineData("2026-06-20", EmploymentWindowStatus.EMPLOYED, false)]     // == end (last day employed)
    [InlineData("2026-06-20", EmploymentWindowStatus.EMPLOYED, true)]
    [InlineData("2026-06-21", EmploymentWindowStatus.NOT_EMPLOYED, false)] // day after end
    [InlineData("2026-06-21", EmploymentWindowStatus.NOT_EMPLOYED, true)]
    public async Task GetStatusAsync_BoundedWindow_BoundaryMatrix(
        string date, EmploymentWindowStatus expected, bool viaInTxOverload)
    {
        var status = await ResolveAsync(viaInTxOverload, BoundedEmployee, D(date));
        Assert.Equal(expected, status);
    }

    // ═════════════════════════════════════════════════════════════════════
    // D2 — NULL start: employed since the beginning of time, end still caps
    // ═════════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData("0001-01-01", EmploymentWindowStatus.EMPLOYED, false)]
    [InlineData("0001-01-01", EmploymentWindowStatus.EMPLOYED, true)]
    [InlineData("2026-06-20", EmploymentWindowStatus.EMPLOYED, false)]
    [InlineData("2026-06-20", EmploymentWindowStatus.EMPLOYED, true)]
    [InlineData("2026-06-21", EmploymentWindowStatus.NOT_EMPLOYED, false)]
    [InlineData("2026-06-21", EmploymentWindowStatus.NOT_EMPLOYED, true)]
    public async Task GetStatusAsync_NullStart_UnboundedIntoThePast(
        string date, EmploymentWindowStatus expected, bool viaInTxOverload)
    {
        var status = await ResolveAsync(viaInTxOverload, NullStartEmployee, D(date));
        Assert.Equal(expected, status);
    }

    // ═════════════════════════════════════════════════════════════════════
    // D2 — NULL end: open-ended employment, start still gates
    // ═════════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData("2026-03-09", EmploymentWindowStatus.NOT_EMPLOYED, false)]
    [InlineData("2026-03-09", EmploymentWindowStatus.NOT_EMPLOYED, true)]
    [InlineData("2026-03-10", EmploymentWindowStatus.EMPLOYED, false)]
    [InlineData("2026-03-10", EmploymentWindowStatus.EMPLOYED, true)]
    [InlineData("9999-12-31", EmploymentWindowStatus.EMPLOYED, false)]
    [InlineData("9999-12-31", EmploymentWindowStatus.EMPLOYED, true)]
    public async Task GetStatusAsync_NullEnd_OpenEnded(
        string date, EmploymentWindowStatus expected, bool viaInTxOverload)
    {
        var status = await ResolveAsync(viaInTxOverload, NullEndEmployee, D(date));
        Assert.Equal(expected, status);
    }

    // ═════════════════════════════════════════════════════════════════════
    // D2 — both NULL: employed on every date (why no backfill is required)
    // ═════════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData("0001-01-01", false)]
    [InlineData("0001-01-01", true)]
    [InlineData("9999-12-31", false)]
    [InlineData("9999-12-31", true)]
    public async Task GetStatusAsync_BothNull_EmployedOnEveryDate(string date, bool viaInTxOverload)
    {
        var status = await ResolveAsync(viaInTxOverload, OpenEmployee, D(date));
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, status);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Missing user ⇒ fail-loud (both overloads)
    // ═════════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStatusAsync_MissingUser_ThrowsInvalidOperationException(bool viaInTxOverload)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(viaInTxOverload, "EMP-WIN-DOES-NOT-EXIST", new DateOnly(2026, 4, 1)));
        Assert.Contains("EMP-WIN-DOES-NOT-EXIST", ex.Message);
    }

    // ═════════════════════════════════════════════════════════════════════
    // D3 — the window ignores is_active: deactivated leaver stays EMPLOYED
    // for in-window dates (and NOT_EMPLOYED past the end — date-driven, not
    // login-state-driven). An is_active copy-paste filter fails this.
    // ═════════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStatusAsync_DeactivatedLeaver_WindowIsDateFactNotLoginState(bool viaInTxOverload)
    {
        // In-window date on an is_active=FALSE, end_date_deactivated=TRUE leaver:
        // this is HR's leaver-correction month — must resolve EMPLOYED.
        Assert.Equal(
            EmploymentWindowStatus.EMPLOYED,
            await ResolveAsync(viaInTxOverload, LeaverEmployee, End));

        // Day after end on the same leaver: NOT_EMPLOYED — proving the answer above
        // came from the dates, not from ignoring the row.
        Assert.Equal(
            EmploymentWindowStatus.NOT_EMPLOYED,
            await ResolveAsync(viaInTxOverload, LeaverEmployee, End.AddDays(1)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // The two-surface rationale, falsifiable: the in-tx surface
    // (IEmploymentWindowResolverInTx) reads THROUGH the caller's transaction
    // (sees its uncommitted window edit); the self-managed surface — a
    // private connection outside the tx — cannot. A future cleanup that
    // "harmonizes" the in-tx surface away into a self-managed read fails
    // the first assertion.
    // ═════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task GetStatusAsync_InTxOverload_SeesUncommittedWindowEdit_SelfManagedDoesNot()
    {
        var probeDate = new DateOnly(2026, 2, 15);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var update = new NpgsqlCommand(
            "UPDATE users SET employment_end_date = @end WHERE user_id = @id", conn, tx))
        {
            update.Parameters.AddWithValue("end", new DateOnly(2026, 1, 31));
            update.Parameters.AddWithValue("id", TxProbeEmployee);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        // In-tx read sees the uncommitted end date ⇒ probe date is now outside the spell.
        Assert.Equal(
            EmploymentWindowStatus.NOT_EMPLOYED,
            await _inTx.GetStatusAsync(conn, tx, TxProbeEmployee, probeDate));

        // Self-managed read (separate pooled connection, READ COMMITTED) cannot see it.
        Assert.Equal(
            EmploymentWindowStatus.EMPLOYED,
            await _selfManaged.GetStatusAsync(TxProbeEmployee, probeDate));

        // The in-tx read must not have committed/closed the caller's tx: rollback still
        // works and restores the both-NULL window (TxContractTests participation contract).
        await tx.RollbackAsync();
        Assert.Equal(
            EmploymentWindowStatus.EMPLOYED,
            await _selfManaged.GetStatusAsync(TxProbeEmployee, probeDate));
    }

    // ═════════════════════════════════════════════════════════════════════
    // S137 / ADR-040 D5 — GetWindowsAsync (the range-scoped, spells-proof
    // list read for segmentation): 0-or-1 entries today; an EMPTY list means
    // "window known, nothing employed in [from, to]" — never "no information".
    // Same D1 (end-inclusive) / D2 (NULL-unbounded) / fail-loud contract as
    // GetStatusAsync.
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetWindowsAsync_OverlappingRange_ReturnsSingleWindowVerbatim()
    {
        var windows = await _selfManaged.GetWindowsAsync(
            BoundedEmployee, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30));

        var window = Assert.Single(windows);
        Assert.Equal(Start, window.Start);
        Assert.Equal(End, window.End);
    }

    /// <summary>Boundary overlap, both fenceposts: a range whose LAST day is the window
    /// start, and one whose FIRST day is the (inclusive) window end, both overlap —
    /// [from, to] and [start, end] are all inclusive (ADR-040 D1).</summary>
    [Theory]
    [InlineData("2026-03-01", "2026-03-10")] // to == start
    [InlineData("2026-06-20", "2026-06-30")] // from == end (last day employed)
    public async Task GetWindowsAsync_RangeTouchingWindowEdge_Overlaps(string from, string to)
    {
        var windows = await _selfManaged.GetWindowsAsync(BoundedEmployee, D(from), D(to));

        Assert.Single(windows);
    }

    /// <summary>Empty list = "window known, no employed day in range" — the caller-side
    /// meaning is fully NOT_EMPLOYED, never "no information" (the planner types every
    /// segment NOT_EMPLOYED on an empty list).</summary>
    [Theory]
    [InlineData("2026-01-01", "2026-03-09")] // range ends the day before the window opens
    [InlineData("2026-06-21", "2026-07-31")] // range starts the day after the last employed day
    public async Task GetWindowsAsync_NonOverlappingRange_ReturnsEmpty(string from, string to)
    {
        var windows = await _selfManaged.GetWindowsAsync(BoundedEmployee, D(from), D(to));

        Assert.Empty(windows);
    }

    /// <summary>D2: a both-NULL window is unbounded and counts as ONE unbounded entry
    /// for ANY range — this is what keeps every existing (windowless) employee EMPLOYED
    /// with no backfill.</summary>
    [Fact]
    public async Task GetWindowsAsync_BothNullWindow_ReturnsOneUnboundedEntry()
    {
        var windows = await _selfManaged.GetWindowsAsync(
            OpenEmployee, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        var window = Assert.Single(windows);
        Assert.Null(window.Start);
        Assert.Null(window.End);
    }

    /// <summary>NULL-sided windows overlap through the unbounded side (D2) and return
    /// their dates verbatim — the planner needs the raw transition dates, not a clamp.</summary>
    [Fact]
    public async Task GetWindowsAsync_NullSidedWindows_OverlapThroughUnboundedSide()
    {
        // NULL start: any range at/before the end date overlaps.
        var nullStart = Assert.Single(await _selfManaged.GetWindowsAsync(
            NullStartEmployee, new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31)));
        Assert.Null(nullStart.Start);
        Assert.Equal(End, nullStart.End);

        // NULL end: any range at/after the start date overlaps.
        var nullEnd = Assert.Single(await _selfManaged.GetWindowsAsync(
            NullEndEmployee, new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 31)));
        Assert.Equal(Start, nullEnd.Start);
        Assert.Null(nullEnd.End);
    }

    /// <summary>Same fail-loud missing-subject contract as GetStatusAsync — a missing
    /// users row is a caller bug, never an empty list.</summary>
    [Fact]
    public async Task GetWindowsAsync_MissingUser_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _selfManaged.GetWindowsAsync(
                "EMP-WIN-DOES-NOT-EXIST", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30)));
        Assert.Contains("EMP-WIN-DOES-NOT-EXIST", ex.Message);
    }

    /// <summary>An inverted range throws instead of returning an empty list — empty is a
    /// legitimate domain answer ("nothing employed in range") and must never mask a
    /// caller's date-arithmetic bug.</summary>
    [Fact]
    public async Task GetWindowsAsync_InvertedRange_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _selfManaged.GetWindowsAsync(
                BoundedEmployee, new DateOnly(2026, 4, 30), new DateOnly(2026, 4, 1)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════

    private static DateOnly D(string isoDate) =>
        DateOnly.Parse(isoDate, CultureInfo.InvariantCulture);

    /// <summary>Routes one resolution through the surface under test. The in-tx path
    /// opens a fresh transaction purely as a carrier (no prior writes), so both surfaces
    /// must agree everywhere except the uncommitted-edit test above.</summary>
    private async Task<EmploymentWindowStatus> ResolveAsync(
        bool viaInTxOverload, string employeeId, DateOnly date)
    {
        if (!viaInTxOverload)
            return await _selfManaged.GetStatusAsync(employeeId, date);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var status = await _inTx.GetStatusAsync(conn, tx, employeeId, date);
        await tx.RollbackAsync();
        return status;
    }

    private async Task SeedUserAsync(
        string id, DateOnly? start, DateOnly? end,
        bool isActive = true, bool endDateDeactivated = false)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active,
                               end_date_deactivated, employment_start_date, employment_end_date)
            VALUES (@id, @id, 'dev-only', @name, NULL,
                    'STY01', 'AC', 'OK24', @isActive,
                    @endDateDeactivated, @start, @end)
            ON CONFLICT (user_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Window {id}");
        cmd.Parameters.AddWithValue("isActive", isActive);
        cmd.Parameters.AddWithValue("endDateDeactivated", endDateDeactivated);
        cmd.Parameters.Add(new NpgsqlParameter("start", NpgsqlTypes.NpgsqlDbType.Date)
        {
            Value = (object?)start ?? DBNull.Value,
        });
        cmd.Parameters.Add(new NpgsqlParameter("end", NpgsqlTypes.NpgsqlDbType.Date)
        {
            Value = (object?)end ?? DBNull.Value,
        });
        await cmd.ExecuteNonQueryAsync();
    }
}
