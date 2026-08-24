namespace StatsTid.Tests.Regression;

/// <summary>
/// S133 / TASK-13305 (QUAL-014) — single source of truth for reading migration DDL out of
/// the SHIPPED <c>docker/postgres/init.sql</c> at test time, instead of pasting a copy of
/// the migration into the test.
///
/// <para>
/// <b>Why this exists (the defect it removes).</b> Four legacy-migration tests (S22 / S25 /
/// S35 / S43) each carried a hand-pasted "verbatim copy" of a migration block from
/// <c>init.sql</c>, annotated with a line-number citation ("lines ~1278-1305"). Those copies
/// had already drifted — every one of the cited line ranges was wrong — and, worse, a copy can
/// silently diverge from what production actually ships: the test then verifies its OWN copy,
/// not the shipped migration, so a real schema regression in <c>init.sql</c> would sail
/// through green (PAT-014 "verification theater"). Extracting the block from the real file at
/// runtime means the test runs EXACTLY what production runs; if the shipped migration drifts,
/// the test's post-migration assertions are what catch it.
/// </para>
///
/// <para>
/// <b>How it stays honest.</b> Every extractor anchors on stable text that already exists in
/// <c>init.sql</c> — the QUOTED <c>schema_migrations</c> ledger id (e.g. <c>'s25-d2-2-version'</c>,
/// which appears only in the real <c>INSERT ... VALUES</c>, never in the surrounding comments)
/// and the <c>DO $$ … $$;</c> guard boundaries. If any anchor goes missing (a rename, a removed
/// block), the extractor throws loudly rather than silently returning the wrong region. This
/// mirrors the S71 / S72 precedent (<c>Slice3bLegacyMigrationTests</c>,
/// <c>SkemaRowPreferencesLegacyMigrationTests</c>), which extract between explicit marker lines;
/// those newer migrations own BEGIN/END markers, whereas the older four are located here by
/// their existing structure so no production file needs to change.
/// </para>
/// </summary>
internal static class CanonicalInitSql
{
    private static string? _cached;

    /// <summary>Full text of the canonical shipped <c>init.sql</c> (read once, then cached).</summary>
    public static string Read() => _cached ??= File.ReadAllText(LocateInitSql());

    /// <summary>
    /// Returns the entire <c>DO $$ … $$;</c> guarded block whose body contains the QUOTED
    /// ledger id <c>'<paramref name="migrationId"/>'</c>. Used for the migrations whose whole
    /// body is one self-contained guarded block (S22, S25) or an ALTER+ledger guarded block
    /// (S35 D1).
    /// </summary>
    public static string ExtractGuardedBlock(string migrationId)
    {
        var sql = Read();
        var idIdx = IndexOfLedgerId(sql, migrationId, startFrom: 0);
        var open = sql.LastIndexOf("DO $$", idIdx, StringComparison.Ordinal);
        if (open < 0)
            throw new InvalidOperationException(
                $"docker/postgres/init.sql: could not find the opening 'DO $$' of the guarded " +
                $"block containing '{migrationId}'. The migration structure changed — update the test.");
        var end = IndexOfBlockClose(sql, idIdx, migrationId);
        return sql.Substring(open, end - open);
    }

    /// <summary>
    /// Returns the contiguous region from <paramref name="startAnchor"/> through the closing
    /// <c>$$;</c> of the guarded ledger block that carries <c>'<paramref name="migrationId"/>'</c>.
    /// Used for S43, whose migration is a bare <c>CREATE TABLE</c> + indexes followed by a small
    /// ledger-only <c>DO $$</c> block (the id is NOT inside the DDL, it is in the trailing ledger).
    /// </summary>
    public static string ExtractRegionThroughGuardedBlock(string startAnchor, string migrationId)
    {
        var sql = Read();
        var start = RequireIndex(sql, startAnchor, startFrom: 0);
        var idIdx = IndexOfLedgerId(sql, migrationId, startFrom: start);
        var end = IndexOfBlockClose(sql, idIdx, migrationId);
        return sql.Substring(start, end - start);
    }

    /// <summary>
    /// Returns the contiguous region from <paramref name="startAnchor"/> through the END of the
    /// first occurrence of <paramref name="endAnchor"/> (inclusive). Used to lift the base
    /// <c>CREATE TABLE IF NOT EXISTS users_audit</c> + its two indexes for S35 — a region that
    /// lives far away from the S35 D1 guarded ALTER block in the shipped file.
    /// </summary>
    public static string ExtractInclusiveRange(string startAnchor, string endAnchor)
    {
        var sql = Read();
        var start = RequireIndex(sql, startAnchor, startFrom: 0);
        var endIdx = RequireIndex(sql, endAnchor, startFrom: start);
        return sql.Substring(start, endIdx + endAnchor.Length - start);
    }

    // ─── internals ────────────────────────────────────────────────────────────

    private static int IndexOfLedgerId(string sql, string migrationId, int startFrom)
    {
        // Anchor on the QUOTED id: it appears only in the real INSERT ... VALUES ('<id>', ...),
        // never in the human-readable comments that also mention the id unquoted.
        var token = "'" + migrationId + "'";
        var idx = sql.IndexOf(token, startFrom, StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidOperationException(
                $"docker/postgres/init.sql no longer contains the migration-id token {token}. " +
                "This test pins that migration — it may have been renamed or removed. Update the " +
                "test or investigate the drift.");
        return idx;
    }

    private static int IndexOfBlockClose(string sql, int fromIdx, string migrationId)
    {
        const string closeToken = "$$;";
        var closeStart = sql.IndexOf(closeToken, fromIdx, StringComparison.Ordinal);
        if (closeStart < 0)
            throw new InvalidOperationException(
                $"docker/postgres/init.sql: could not find the closing '$$;' of the guarded block " +
                $"containing '{migrationId}'. The migration structure changed — update the test.");
        return closeStart + closeToken.Length;
    }

    private static int RequireIndex(string sql, string anchor, int startFrom)
    {
        var idx = sql.IndexOf(anchor, startFrom, StringComparison.Ordinal);
        if (idx < 0)
            throw new InvalidOperationException(
                $"docker/postgres/init.sql no longer contains the anchor text \"{anchor}\". " +
                "This test pins the migration that owned it — update the test or investigate the drift.");
        return idx;
    }

    private static string LocateInitSql()
    {
        // Walk up from the test bin dir (the csproj copies init.sql alongside the output as
        // docker/postgres/init.sql, so this resolves on iteration 0 in CI). Mirrors
        // StatsTidWebApplicationFactory.LocateInitSql / the S71-S72 precedent.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docker", "postgres", "init.sql");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate docker/postgres/init.sql by walking up from " +
            $"AppContext.BaseDirectory='{AppContext.BaseDirectory}'.");
    }
}
