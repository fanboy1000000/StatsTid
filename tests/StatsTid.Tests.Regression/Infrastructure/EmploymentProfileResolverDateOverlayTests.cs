using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Infrastructure;

/// <summary>
/// S137 / TASK-13703 (ADR-040 D4 — closes QUAL-147) — contract tests for the reworked
/// <see cref="EmploymentProfileResolver"/>, driven directly against the real schema.
///
/// <para>
/// <b>What changed (plain language).</b> Which collective-agreement version (OK24 / OK26) governs
/// a date is fixed by the calendar, so the resolver now derives <c>OkVersion</c> from the as-of
/// DATE (<c>OkVersionResolver</c>, ADR-003) instead of copying the live <c>users.ok_version</c>
/// column — a March-2026 read for an employee whose row already says OK26 now correctly says OK24,
/// for EVERY consumer at once (compliance, balances, payroll) with no per-caller overlay left to
/// forget. <c>EmploymentCategory</c> now reads the DATED <c>employee_profiles</c> cell, degrading to
/// the live value only when the cell is NULL (the Wave 1 COALESCE posture).
/// </para>
///
/// <para>
/// <b>What did NOT change (ADR-023 D3 / ADR-040 D10).</b> No covering profile row ⇒ <c>null</c>
/// (never a throw); a profile row WITHOUT an agreement-code row covering the same date ⇒ the
/// fail-loud <see cref="EmployeeProfileNotFoundException"/> (a seeding bug, not a domain state).
/// Both pinned here so the rework cannot have drifted them.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentProfileResolverDateOverlayTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string LiveCategory = "Fuldmægtig";      // non-default, so defaults cannot mask a miss
    private const string DivergedCategory = "Chefkonsulent";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private EmploymentProfileResolver _resolver = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _resolver = new EmploymentProfileResolver(_harness.Factory, new UserAgreementCodeRepository(_harness.Factory));
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // OK version — a pure function of the as-of date, never the live column.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A live-OK26 user (<c>users.ok_version = 'OK26'</c>) read at OK24-era dates resolves
    /// <c>OK24</c>; from 2026-04-01 it resolves <c>OK26</c>. Pre-S137 every one of these reads
    /// returned the live <c>OK26</c> — the QUAL-147 defect.
    /// </summary>
    [Theory]
    [InlineData("2025-12-01", "OK24")]
    [InlineData("2026-03-15", "OK24")]
    [InlineData("2026-03-31", "OK24")]   // last OK24 day
    [InlineData("2026-04-01", "OK26")]   // first OK26 day
    [InlineData("2026-09-30", "OK26")]
    public async Task OkVersion_ResolvesFromAsOfDate_NotFromLiveUsersColumn(string asOf, string expectedOk)
    {
        var employeeId = await SeedAsync(okVersion: "OK26");
        Assert.Equal("OK26", await ReadUsersOkVersionAsync(employeeId));

        var profile = await _resolver.GetByEmployeeIdAtAsync(employeeId, DateOnly.Parse(asOf));

        Assert.NotNull(profile);
        Assert.Equal(expectedOk, profile!.OkVersion);
    }

    // ════════════════════════════════════════════════════════════════════════
    // employment_category — dated cell preferred, COALESCE to live on NULL.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The seeded profile row carries a NULL dated category (the RegressionSeed shape — a row a
    /// write path "missed"): the read degrades to the live users value (dated == live by the
    /// fail-safe), never NULL / crash.
    /// </summary>
    [Fact]
    public async Task EmploymentCategory_DatedCellNull_DegradesToLiveValue()
    {
        var employeeId = await SeedAsync(okVersion: "OK24");
        Assert.True(await DatedCategoryIsNullAsync(employeeId));

        var profile = await _resolver.GetByEmployeeIdAtAsync(employeeId, new DateOnly(2026, 3, 15));

        Assert.Equal(LiveCategory, profile!.EmploymentCategory);
    }

    /// <summary>
    /// A populated dated cell WINS over the live value (the dated column is the source of truth
    /// once written); and when both agree (the S137 invariant) the read equals the live value.
    /// </summary>
    [Fact]
    public async Task EmploymentCategory_DatedCellSet_WinsOverLive_AndEqualsLiveWhenEqual()
    {
        var employeeId = await SeedAsync(okVersion: "OK24");

        await SetDatedCategoryAsync(employeeId, DivergedCategory);
        var diverged = await _resolver.GetByEmployeeIdAtAsync(employeeId, new DateOnly(2026, 3, 15));
        Assert.Equal(DivergedCategory, diverged!.EmploymentCategory);           // dated wins
        Assert.Equal(LiveCategory, await ReadUsersCategoryAsync(employeeId));   // live untouched

        await SetDatedCategoryAsync(employeeId, LiveCategory);
        var aligned = await _resolver.GetByEmployeeIdAtAsync(employeeId, new DateOnly(2026, 3, 15));
        Assert.Equal(LiveCategory, aligned!.EmploymentCategory);                // dated == live
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADR-023 D3 semantics — UNCHANGED by the rework.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>No profile row covers the as-of (rows start at the 2026-05-10 hire) ⇒ null, no throw.</summary>
    [Fact]
    public async Task NoCoveringRow_ReturnsNull_NeverThrows()
    {
        var hire = new DateOnly(2026, 5, 10);
        var employeeId = await SeedAsync(okVersion: "OK24", effectiveFrom: hire);

        Assert.Null(await _resolver.GetByEmployeeIdAtAsync(employeeId, new DateOnly(2026, 4, 15)));
        Assert.Null(await _resolver.GetByEmployeeIdAtAsync(employeeId, hire.AddDays(-1)));
        Assert.NotNull(await _resolver.GetByEmployeeIdAtAsync(employeeId, hire)); // effective_from inclusive
    }

    /// <summary>
    /// A profile row WITHOUT an agreement-code row covering the same date is a data-integrity
    /// fault ⇒ the fail-loud exception (still, post-rework).
    /// </summary>
    [Fact]
    public async Task ProfileRowWithoutAgreementRow_ThrowsFailLoud_Unchanged()
    {
        var employeeId = await SeedAsync(okVersion: "OK24");
        await DeleteAgreementCodeRowsAsync(employeeId);

        await Assert.ThrowsAsync<EmployeeProfileNotFoundException>(
            () => _resolver.GetByEmployeeIdAtAsync(employeeId, new DateOnly(2026, 3, 15)));
    }

    /// <summary>The other dated fields still flow: fraction/position from the row; agreement dated; org live.</summary>
    [Fact]
    public async Task OtherFields_StillHydrated()
    {
        var employeeId = await SeedAsync(okVersion: "OK24", partTimeFraction: 0.800m, position: "Specialist");

        var profile = await _resolver.GetByEmployeeIdAtAsync(employeeId, new DateOnly(2026, 3, 15));

        Assert.NotNull(profile);
        Assert.Equal(employeeId, profile!.EmployeeId);
        Assert.Equal(0.800m, profile.PartTimeFraction);
        Assert.True(profile.IsPartTime);
        Assert.Equal("Specialist", profile.Position);
        Assert.Equal("AC", profile.AgreementCode);
        Assert.Equal(OrgId, profile.OrgId);
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<string> SeedAsync(
        string okVersion, DateOnly? effectiveFrom = null, decimal partTimeFraction = 1.000m, string? position = null)
    {
        var employeeId = "emp_s137_ovl_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, OrgId, "AC", okVersion,
            partTimeFraction: partTimeFraction, effectiveFrom: effectiveFrom, position: position);
        // A NON-default live category so a COALESCE miss could never pass by coincidence of defaults.
        await SetUsersCategoryAsync(employeeId, LiveCategory);
        return employeeId;
    }

    // Each helper carries its SQL as a LITERAL (CA2100: the CI ratchet counts non-constant
    // command text in test projects too — a generic Exec(sql) helper would add sites).

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private async Task SetUsersCategoryAsync(string employeeId, string category)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_category = @c WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("c", category);
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task SetDatedCategoryAsync(string employeeId, string category)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE employee_profiles SET employment_category = @c WHERE employee_id = @id AND effective_to IS NULL", conn);
        cmd.Parameters.AddWithValue("c", category);
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task DeleteAgreementCodeRowsAsync(string employeeId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM user_agreement_codes WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.True(await cmd.ExecuteNonQueryAsync() >= 1);
    }

    private async Task<string> ReadUsersOkVersionAsync(string employeeId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT ok_version FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<string> ReadUsersCategoryAsync(string employeeId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT employment_category FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<bool> DatedCategoryIsNullAsync(string employeeId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT employment_category IS NULL FROM employee_profiles WHERE employee_id = @id AND effective_to IS NULL", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}
