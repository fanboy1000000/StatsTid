using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

public sealed class PayrollMappingService
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ILogger<PayrollMappingService> _logger;

    public PayrollMappingService(DbConnectionFactory connectionFactory, ILogger<PayrollMappingService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Position-aware wage type mapping lookup.
    /// Canonical convention: empty string (<c>''</c>) in the <c>position</c> column is the
    /// generic fallback. The DB column is NOT NULL with DEFAULT '', so we must never query
    /// <c>position IS NULL</c> (Codex BLOCKER fix, TASK-1802).
    /// Precedence: position-specific match wins over the generic ('') row.
    /// </summary>
    public async Task<WageTypeMapping?> GetMappingAsync(
        string timeType, string okVersion, string agreementCode, string? position = null, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        NpgsqlCommand cmd;

        // Treat null or empty position identically as "generic lookup".
        if (!string.IsNullOrEmpty(position))
        {
            // Position provided: prefer position-specific row, fall back to generic ('').
            // ORDER BY (position = '') ASC puts the specific match first (false < true).
            cmd = new NpgsqlCommand(
                """
                SELECT time_type, wage_type, ok_version, agreement_code, position, description
                FROM wage_type_mappings
                WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
                  AND (position = @position OR position = '')
                ORDER BY (position = '') ASC
                LIMIT 1
                """, conn);
            cmd.Parameters.AddWithValue("timeType", timeType);
            cmd.Parameters.AddWithValue("okVersion", okVersion);
            cmd.Parameters.AddWithValue("agreementCode", agreementCode);
            cmd.Parameters.AddWithValue("position", position);
        }
        else
        {
            // Generic-only lookup: match the canonical empty-string row.
            cmd = new NpgsqlCommand(
                """
                SELECT time_type, wage_type, ok_version, agreement_code, position, description
                FROM wage_type_mappings
                WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
                  AND position = ''
                LIMIT 1
                """, conn);
            cmd.Parameters.AddWithValue("timeType", timeType);
            cmd.Parameters.AddWithValue("okVersion", okVersion);
            cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        }

        await using (cmd)
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                _logger.LogWarning("No mapping found for {TimeType}/{OkVersion}/{Agreement}/{Position}",
                    timeType, okVersion, agreementCode, string.IsNullOrEmpty(position) ? "(generic)" : position);
                return null;
            }

            // position column is NOT NULL (DEFAULT ''), so GetString is safe.
            return new WageTypeMapping
            {
                TimeType = reader.GetString(0),
                WageType = reader.GetString(1),
                OkVersion = reader.GetString(2),
                AgreementCode = reader.GetString(3),
                Position = reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5)
            };
        }
    }

    /// <summary>
    /// Maps a CalculationResult to PayrollExportLines using wage type mappings.
    /// Accepts optional position for position-aware mapping lookup; null/empty == generic.
    ///
    /// <para>
    /// OK-version stamping (TASK-2010, ADR-016 D5): the per-line OK version is resolved from
    /// each <see cref="CalculationLineItem.Date"/> via <see cref="OkVersionResolver.ResolveVersion"/>.
    /// This supersedes any caller-supplied <c>profile.OkVersion</c> at the export boundary
    /// — the same resolved value flows into both the wage-type-mapping lookup and the
    /// emitted <see cref="PayrollExportLine.OkVersion"/>, so a straddling export across an
    /// OK transition produces correctly per-line-stamped lines (Step 0b WARNING W3 bound:
    /// only OK version is per-line resolved; wage-type-mapping itself remains non-dated /
    /// snapshot-at-calculation per ADR-016 D5b).
    /// </para>
    ///
    /// <para>
    /// Manifest linkage (ADR-016 D10): the optional <paramref name="manifestId"/> is stamped
    /// onto every emitted line. Default <see cref="Guid.Empty"/> preserves callers that do
    /// not yet thread the manifest id (e.g. tests, pre-S20 fixtures).
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<PayrollExportLine>> MapCalculationResultAsync(
        CalculationResult result,
        EmploymentProfile profile,
        string? position = null,
        Guid manifestId = default,
        CancellationToken ct = default)
    {
        // Silent-loss guard (post-S20 cleanup, Reviewer wave-2 WARNING): the caller has the
        // manifest id available on result.ManifestId but did not thread it into the optional
        // parameter — emitted lines would carry Guid.Empty in PayrollExportLine.ManifestId,
        // breaking the audit chain without any compile-time or runtime signal. Warn loudly;
        // do not throw (legacy callers without manifest plumbing remain valid).
        if (result.ManifestId != Guid.Empty && manifestId == Guid.Empty)
        {
            _logger.LogWarning(
                "MapCalculationResultAsync: result.ManifestId={ResultManifestId} but caller passed " +
                "manifestId=Guid.Empty. Emitted PayrollExportLines will carry empty ManifestId, " +
                "breaking the audit chain. Thread result.ManifestId through to close the gap.",
                result.ManifestId);
        }

        var lines = new List<PayrollExportLine>();

        foreach (var lineItem in result.LineItems)
        {
            // Per-line OK supersedes profile.OkVersion at the export boundary (ADR-016 D5,
            // Step 0b W3): resolves from CalculationLineItem.Date so a straddling export
            // across an OK transition stamps each line with the segment-resolved OK.
            var lineOkVersion = OkVersionResolver.ResolveVersion(lineItem.Date);

            var mapping = await GetMappingAsync(lineItem.TimeType, lineOkVersion, profile.AgreementCode, position, ct);
            if (mapping is null) continue;

            // Per-line semantics: PeriodStart == PeriodEnd == lineItem.Date. The /export and
            // /export-period endpoints don't have segment knowledge — each line stands alone
            // at its entry date. Contrast with the planner-driven path in PCS which sets
            // PeriodStart/End to the segment range; both go through BuildLine for the
            // canonical line shape (post-S20 cleanup; previously two near-duplicate
            // constructors in PCS and here).
            lines.Add(BuildLine(
                lineItem, result, profile, mapping,
                periodStart: lineItem.Date,
                periodEnd: lineItem.Date,
                okVersion: lineOkVersion,
                manifestId: manifestId));
        }

        return lines;
    }

    /// <summary>
    /// Canonical <see cref="PayrollExportLine"/> shape constructor — one source of truth for
    /// per-line semantics across both per-line-date callers (this service's
    /// <see cref="MapCalculationResultAsync"/> serving <c>/export</c> and
    /// <c>/export-period</c>) and per-segment callers
    /// (<c>PeriodCalculationService.MapSegmentToExportLinesAsync</c> serving the planner-driven
    /// path). The two callers differ in <paramref name="periodStart"/>/<paramref name="periodEnd"/>
    /// and <paramref name="okVersion"/> resolution; everything else is identical.
    ///
    /// <para>
    /// Internal so PCS can call it without re-exposing the type via a public surface;
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/> on
    /// <c>StatsTid.Integrations.Payroll</c> is not needed because both consumers live in the
    /// same assembly.
    /// </para>
    /// </summary>
    internal static PayrollExportLine BuildLine(
        CalculationLineItem lineItem,
        CalculationResult result,
        EmploymentProfile profile,
        WageTypeMapping mapping,
        DateOnly periodStart,
        DateOnly periodEnd,
        string okVersion,
        Guid manifestId)
        => new()
        {
            EmployeeId = profile.EmployeeId,
            WageType = mapping.WageType,
            Hours = lineItem.Hours,
            Amount = lineItem.Hours * lineItem.Rate,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            OkVersion = okVersion,
            SourceRuleId = result.RuleId,
            SourceTimeType = lineItem.TimeType,
            ManifestId = manifestId,
        };
}
