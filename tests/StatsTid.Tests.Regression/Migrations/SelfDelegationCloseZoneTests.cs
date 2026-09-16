using Npgsql;
using StatsTid.Tests.Regression.Hosting;

namespace StatsTid.Tests.Regression.Migrations;

/// <summary>
/// S142 / TASK-14208 (owner ruling OQ-8) — pins on the <c>s74-medarbejder-admin-foundations</c>
/// migration block in <c>docker/postgres/init.sql</c>: the SELF_DELEGATION → manager_vikar
/// projection and the <c>UPDATE reporting_lines</c> that closes the projected ACTING rows.
///
/// <para>
/// <b>What this is about, for a reader who is not the code's author.</b> When a Danish manager goes
/// on holiday they delegate approval to a stand-in ("vikar"). Sprint 74 moved that from a fan-out of
/// per-employee ACTING reporting lines to one row per delegating manager, and the migration block
/// closes the old rows by stamping the day they stopped applying. That day is a BUSINESS date, and
/// everyone using StatsTid is Danish — but the statement asked Postgres via <c>CURRENT_DATE</c>, so
/// the day came from whatever timezone the database container happened to run in: a value nobody
/// sets, sees or can test. S142 converts it in place to <c>Europe/Copenhagen</c>, so the rule ("a
/// business date is a Danish calendar day") holds everywhere with no documented exception.
/// </para>
///
/// <para>
/// <b>The false comment these tests exist to keep dead.</b> The block was introduced with a comment
/// claiming <i>"On a greenfield DB this is a no-op (no open SELF_DELEGATION ACTING rows exist)"</i>.
/// That was never true: the S52 seed inserts an ACTING SELF_DELEGATION row for <c>emp005</c> and
/// supplies no <c>effective_to</c>, so the column is NULL and the <c>UPDATE</c> 1,500 lines later
/// matches it on EVERY greenfield database. The claim survived long enough to make a careful
/// reviewer conclude the <c>CURRENT_DATE</c> could never execute. A comment cannot be trusted to
/// stay honest on its own, so the two facts it got wrong are pinned as tests instead: the seed
/// really is open-ended (<see cref="SeedRow_IsOpenEnded_SoTheMigrationBlockIsNotAGreenfieldNoOp"/>)
/// and the block really does fire on greenfield
/// (<see cref="SelfDelegationCloseGreenfieldTests.Greenfield_TheCloseUpdateFires_StampingTheSeededActingRow"/>).
/// </para>
///
/// <para>
/// <b>Why these assertions and not the obvious one.</b> The obvious test — read the stored
/// <c>effective_to</c> back and compare it against "today in Copenhagen" — is one of the defects
/// this sprint removes, not a check on it. No <see cref="TimeProvider"/> can reach the database's
/// own clock, so that comparison would either be self-referential (the same zone computed on both
/// sides, passing no matter which zone the statement used) or flaky for the minutes either side of
/// midnight. What IS deterministic and clock-independent is (a) that the statement fired at all,
/// and (b) what the statement SAYS. Both are pinned; the exact date it stamps is deliberately not.
/// </para>
///
/// <para>
/// This class needs no database — it reads the shipped <c>init.sql</c> through
/// <see cref="CanonicalInitSql"/> (the S133/QUAL-014 single source of truth, so the test inspects
/// exactly what production applies rather than a pasted copy), the text-assertion precedent being
/// <c>ScheduledChangeMarkerTests</c>. The Docker-gated companion below proves the effect.
/// </para>
/// </summary>
public sealed class SelfDelegationCloseZoneTests
{
    private const string MigrationId = "s74-medarbejder-admin-foundations";

    /// <summary>The S52 self-delegation seed, quoted exactly as it appears in init.sql.</summary>
    private const string SeedValuesRow =
        "VALUES ('emp005', 'ladm01', 'STY02', 'ACTING', '2026-06-01', 'SELF_DELEGATION', 'mgr01', '2026-07-01')";

    // ── The statement: the close-stamp is a Danish calendar day ──────────────────────────────────

    /// <summary>
    /// The close-stamp is derived through the real <c>Europe/Copenhagen</c> zone. Postgres'
    /// <c>AT TIME ZONE</c> is DST-aware — it reads the tz database, so the same expression gives
    /// UTC+1 in January and UTC+2 in July. A hardcoded <c>+01:00</c> offset would have been wrong
    /// every summer (the QUAL-005 bug the SharedKernel helper was written to end).
    /// </summary>
    [Fact]
    public void CloseUpdate_StampsTheCopenhagenCalendarDay()
    {
        var block = CanonicalInitSql.ExtractGuardedBlock(MigrationId);

        Assert.Contains("UPDATE reporting_lines", block, StringComparison.Ordinal);
        Assert.Contains(
            "SET effective_to = (NOW() AT TIME ZONE 'Europe/Copenhagen')::date",
            block,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The server clock is GONE from the block, not merely supplemented. <c>CURRENT_DATE</c> is the
    /// exact token that made the day depend on the database container's timezone; if it reappears
    /// in this migration's EXECUTABLE SQL the conversion has been partially undone.
    ///
    /// <para>
    /// <c>--</c> comments are stripped first, deliberately. The block's own prose now explains what
    /// <c>CURRENT_DATE</c> used to do and why it went — writing that history down is the point of
    /// this change, and a pin that forbade even naming the token would punish the explanation while
    /// proving nothing about the statement. (This test earned its keep immediately: the first
    /// version of it failed on that very comment.)
    /// </para>
    /// </summary>
    [Fact]
    public void CloseUpdate_NoLongerAsksTheDatabaseServerWhatDayItIs()
    {
        var executableSql = StripSqlComments(CanonicalInitSql.ExtractGuardedBlock(MigrationId));

        Assert.DoesNotContain("CURRENT_DATE", executableSql, StringComparison.Ordinal);
        // Sanity: stripping did not eat the statement the assertion is about.
        Assert.Contains("UPDATE reporting_lines", executableSql, StringComparison.Ordinal);
    }

    /// <summary>Drops every <c>--</c> line comment, leaving the executable SQL.</summary>
    private static string StripSqlComments(string sql)
    {
        var kept = sql
            .Split('\n')
            .Select(line =>
            {
                var idx = line.IndexOf("--", StringComparison.Ordinal);
                return idx < 0 ? line : line[..idx];
            });
        return string.Join('\n', kept);
    }

    // ── The premise: the block is NOT a greenfield no-op ─────────────────────────────────────────

    /// <summary>
    /// The seed row the <c>UPDATE</c> matches. Two facts make it match, and both are pinned here
    /// because the retired comment denied the conclusion they force: the row is
    /// <c>SELF_DELEGATION</c> + <c>ACTING</c>, and the INSERT's column list has no
    /// <c>effective_to</c>, so that column is NULL — which is exactly the <c>UPDATE</c>'s WHERE
    /// clause. Add <c>effective_to</c> to this seed and the greenfield behaviour changes silently;
    /// this is what says so out loud.
    /// </summary>
    [Fact]
    public void SeedRow_IsOpenEnded_SoTheMigrationBlockIsNotAGreenfieldNoOp()
    {
        var sql = CanonicalInitSql.Read();

        var seedIdx = sql.IndexOf(SeedValuesRow, StringComparison.Ordinal);
        Assert.True(
            seedIdx > 0,
            "docker/postgres/init.sql no longer carries the S52 self-delegation seed row verbatim. " +
            "If it was changed, re-check whether the s74 migration block still fires on a greenfield " +
            "database — a previous comment claimed it does not, and that was false.");

        // The INSERT's column list sits immediately above the VALUES row.
        var insertIdx = sql.LastIndexOf("INSERT INTO reporting_lines (", seedIdx, StringComparison.Ordinal);
        Assert.True(insertIdx > 0, "Could not find the INSERT that owns the S52 self-delegation seed row.");
        var columnList = sql.Substring(insertIdx, seedIdx - insertIdx);

        Assert.Contains("source", columnList, StringComparison.Ordinal);
        Assert.DoesNotContain("effective_to", columnList, StringComparison.Ordinal);
    }

    /// <summary>
    /// The <c>UPDATE</c>'s WHERE clause is the other half of the meeting: it selects precisely the
    /// shape the seed above has. Stated as its own fact so that narrowing either side (say, adding
    /// a <c>scheduled_expiry IS NOT NULL</c> filter to match the INSERT's) is a deliberate, visible
    /// change rather than a quiet one.
    /// </summary>
    [Fact]
    public void CloseUpdate_TargetsExactlyTheOpenSelfDelegationActingRows()
    {
        var block = CanonicalInitSql.ExtractGuardedBlock(MigrationId);
        var updateIdx = block.IndexOf("UPDATE reporting_lines", StringComparison.Ordinal);
        Assert.True(updateIdx > 0);
        var update = block.Substring(updateIdx);

        Assert.Contains("source = 'SELF_DELEGATION'", update, StringComparison.Ordinal);
        Assert.Contains("relationship = 'ACTING'", update, StringComparison.Ordinal);
        Assert.Contains("effective_to IS NULL", update, StringComparison.Ordinal);
    }

    /// <summary>
    /// The retired claim itself. Asserted on the COMMENT region only (from the section heading down
    /// to the <c>DO $$</c>), because that is the text a reader reaches for before deciding whether
    /// the block below can execute — and it is where the false sentence actually misled someone.
    ///
    /// <para>
    /// The comparison runs over prose with the <c>--</c> markers and line wrapping removed, because
    /// the original sentence was SPLIT across two comment lines ("On a greenfield DB this is a" /
    /// "--   no-op …"). A naive whitespace-normalise leaves the marker mid-sentence and the
    /// assertion then passes against the very text it is supposed to forbid — a false green, which
    /// is worse than no test.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBlockIsNoLongerDescribedAsAGreenfieldNoOp()
    {
        var comment = CanonicalInitSql.ExtractInclusiveRange("-- (3) SELF_DELEGATION", "DO $$");
        var prose = NormalizeCommentProse(comment);

        Assert.DoesNotContain("On a greenfield DB this is a no-op", prose, StringComparison.Ordinal);
        // And it says the opposite, in so many words, so the next reader is told rather than left
        // to work it out from a seed 1,500 lines away.
        Assert.Contains("EVERY greenfield database", prose, StringComparison.Ordinal);
    }

    /// <summary>
    /// Turns a wrapped SQL comment block into one line of prose: drop each line's leading
    /// <c>--</c> marker, then collapse all whitespace. A sentence wrapped across comment lines
    /// therefore compares as the sentence it reads as.
    /// </summary>
    private static string NormalizeCommentProse(string commentBlock)
    {
        var stripped = commentBlock
            .Split('\n')
            .Select(line =>
            {
                var trimmed = line.TrimStart();
                return trimmed.StartsWith("--", StringComparison.Ordinal) ? trimmed[2..] : trimmed;
            });
        return string.Join(' ', string.Join('\n', stripped)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// S142 / TASK-14208 — the Docker-gated half of <see cref="SelfDelegationCloseZoneTests"/>: apply
/// the real, whole <c>init.sql</c> to an empty Postgres and show that the S74 block FIRES.
///
/// <para>
/// This is the fact that falsifies the retired "greenfield no-op" comment by observation rather
/// than by reading. It asserts nothing about WHICH day was stamped — only that a day was, and that
/// the row's version moved — because those are the parts no clock can make ambiguous. See the
/// companion class for why a date comparison here would prove nothing.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SelfDelegationCloseGreenfieldTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() =>
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// On a freshly-created database the S74 block closes the seeded <c>emp005</c> ACTING row and
    /// projects the manager_vikar row that replaces it.
    ///
    /// <para>
    /// <b>Every assertion here is clock-independent.</b> <c>effective_to IS NOT NULL</c> is true
    /// whichever calendar day the statement resolved; <c>version = 2</c> follows from the column's
    /// <c>DEFAULT 1</c> plus the statement's <c>version + 1</c>, and no timezone can change it. If
    /// the block really were the greenfield no-op its comment used to claim, <c>effective_to</c>
    /// would still be NULL and the version still 1 — so this single fact is what disproves the
    /// claim, permanently.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Greenfield_TheCloseUpdateFires_StampingTheSeededActingRow()
    {
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            """
            SELECT effective_to IS NOT NULL, version
            FROM reporting_lines
            WHERE employee_id = 'emp005'
              AND manager_id = 'ladm01'
              AND source = 'SELF_DELEGATION'
              AND relationship = 'ACTING'
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            Assert.True(
                await reader.ReadAsync(),
                "The S52 self-delegation seed row is missing — the premise of this pin is gone.");
            Assert.True(reader.GetBoolean(0), "effective_to was not stamped: the s74 close UPDATE did not fire.");
            Assert.Equal(2L, reader.GetInt64(1)); // DEFAULT 1, bumped once by the UPDATE
            Assert.False(await reader.ReadAsync(), "Expected exactly one seeded SELF_DELEGATION ACTING row.");
        }

        // No open SELF_DELEGATION ACTING rows remain anywhere — the fan-out is fully closed.
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM reporting_lines
            WHERE source = 'SELF_DELEGATION' AND relationship = 'ACTING' AND effective_to IS NULL
            """, conn))
        {
            Assert.Equal(0L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
        }

        // …and the projection half of the same block ran too: one manager_vikar row for the
        // delegating manager (mgr01), which is the row the closed ACTING line was swapped for.
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM manager_vikar
            WHERE absent_approver_id = 'mgr01' AND effective_to IS NULL
            """, conn))
        {
            Assert.Equal(1L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
        }
    }
}
