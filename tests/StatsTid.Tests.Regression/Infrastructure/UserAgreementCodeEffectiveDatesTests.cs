using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;

namespace StatsTid.Tests.Regression.Infrastructure;

/// <summary>
/// S144 / TASK-14409 (reviewer WARNING 14403-1) — fencepost pins for
/// <see cref="UserAgreementCodeRepository.GetEffectiveFromDatesAsync"/>, the payroll planner's
/// <c>AgreementCodeChange</c> segment-boundary feed.
///
/// <para>
/// Plain language: the planner asks "on which days did this employee's agreement change inside
/// this month?". A day counts when a <c>user_agreement_codes</c> row STARTS on it, strictly after the
/// period start (a row starting ON the first day creates no interior boundary) and up to and
/// including the last day. Getting either edge wrong is silent: a missed boundary pays part of the
/// month under the wrong agreement; a spurious one only sends the month to manual handling. These
/// facts pin each edge separately so a single-character predicate mutation trips exactly one of them.
/// </para>
///
/// <para>
/// Mirrors the four profile-sibling fencepost pins in <c>ProfileCategoryDatingTests</c>
/// (<c>GetEffectiveFromDates_StrictlyAfterLowerBound_…</c>), but split into separate facts and
/// covering the closed and zero-width rows the repository deliberately does NOT filter out.
/// The table key is <c>user_id</c>. Window used throughout: afterExclusive 2026-03-01,
/// toInclusive 2026-03-31.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class UserAgreementCodeEffectiveDatesTests : IAsyncLifetime
{
    private static readonly DateOnly Start = new(2026, 3, 1);
    private static readonly DateOnly End = new(2026, 3, 31);

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private UserAgreementCodeRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _repo = new UserAgreementCodeRepository(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// A row effective ON <c>afterExclusive</c> (the 1st) is excluded; the 2nd is the first date in.
    /// <b>Red conditions:</b> changing <c>effective_from &gt; @afterExclusive</c> to <c>&gt;=</c>
    /// returns 2026-03-01 and trips the assertion.
    /// </summary>
    [Fact]
    public async Task GetEffectiveFromDates_RowOnAfterExclusive_IsExcluded_NextDayIsIncluded()
    {
        var user = await CreateUserAsync();
        await InsertRowAsync(user, "2026-03-01", "2026-03-02", "HK");
        await InsertRowAsync(user, "2026-03-02", null, "AC");

        var dates = await _repo.GetEffectiveFromDatesAsync(user, Start, End);

        Assert.Equal(new[] { new DateOnly(2026, 3, 2) }, dates);
    }

    /// <summary>
    /// Rows on the 2nd and ON <c>toInclusive</c> (the 31st) are both returned; a row on 2026-04-01
    /// stays out. <b>Red conditions:</b> changing <c>effective_from &lt;= @toInclusive</c> to
    /// <c>&lt;</c> drops 2026-03-31 and trips the last-day half.
    /// </summary>
    [Fact]
    public async Task GetEffectiveFromDates_RowsOnSecondAndOnToInclusive_AreIncluded()
    {
        var user = await CreateUserAsync();
        await InsertRowAsync(user, "2026-03-02", "2026-03-31", "HK");
        await InsertRowAsync(user, "2026-03-31", "2026-04-01", "AC");
        await InsertRowAsync(user, "2026-04-01", null, "HK");

        var dates = await _repo.GetEffectiveFromDatesAsync(user, Start, End);

        Assert.Equal(
            new[] { new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 31) },
            dates);
    }

    /// <summary>
    /// A closed row and a zero-width row (<c>effective_to = effective_from</c>) inside the window
    /// are both returned — there is deliberately no <c>effective_to</c> filter (over-detecting a
    /// boundary is safe; under-detecting pays the month under the wrong agreement).
    /// <b>Red conditions:</b> adding <c>AND effective_to IS NULL</c> drops the closed and the
    /// zero-width row and trips this fact.
    /// </summary>
    [Fact]
    public async Task GetEffectiveFromDates_ClosedRow_AndZeroWidthRow_AreBothReturned()
    {
        var user = await CreateUserAsync();
        await InsertRowAsync(user, "2026-03-10", "2026-03-20", "HK");   // closed, non-empty
        await InsertRowAsync(user, "2026-03-20", "2026-03-20", "AC");   // zero-width
        await InsertRowAsync(user, "2026-03-25", null, "HK");           // live

        var dates = await _repo.GetEffectiveFromDatesAsync(user, Start, End);

        Assert.Equal(
            new[] { new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 25) },
            dates);
    }

    /// <summary>
    /// Ascending and distinct, and another user's rows never leak in. Rows are inserted out of date
    /// order. <b>Red conditions:</b> dropping <c>ORDER BY effective_from</c> can return storage
    /// order (trips the ascending equality); removing <c>user_id = @userId</c> leaks the other
    /// user's 2026-03-12 and 2026-03-15 (trips the equality). DISTINCT is also guaranteed by the
    /// unique (user_id, effective_from) index, so it is not independently falsifiable here.
    /// </summary>
    [Fact]
    public async Task GetEffectiveFromDates_IsAscending_AndScopedToTheEmployee()
    {
        var user = await CreateUserAsync();
        var other = await CreateUserAsync();
        await InsertRowAsync(user, "2026-03-25", null, "HK");
        await InsertRowAsync(user, "2026-03-05", "2026-03-14", "AC");
        await InsertRowAsync(user, "2026-03-14", "2026-03-25", "HK");
        await InsertRowAsync(other, "2026-03-12", "2026-03-15", "AC");
        await InsertRowAsync(other, "2026-03-15", null, "HK");

        var dates = await _repo.GetEffectiveFromDatesAsync(user, Start, End);

        Assert.Equal(
            new[] { new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 14), new DateOnly(2026, 3, 25) },
            dates);
        Assert.Equal(dates.Distinct().Count(), dates.Count);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private async Task<string> CreateUserAsync()
    {
        var userId = "emp_s144_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        await ExecAsync(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, employment_category, is_active)
            VALUES (@p0, @p0, 'dev-only', 'S144 Agreement Dates User', NULL,
                    'STY01', 'AC', 'OK24', 'Standard', TRUE)
            """, userId);
        return userId;
    }

    private Task InsertRowAsync(string userId, string from, string? to, string agreementCode)
        => ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to)
            VALUES (@p0, @p1, @p2, @p3, @p4)
            """,
            Guid.NewGuid(), userId, agreementCode, DateOnly.Parse(from),
            to is null ? DBNull.Value : DateOnly.Parse(to));

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values
        // go through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }
}
