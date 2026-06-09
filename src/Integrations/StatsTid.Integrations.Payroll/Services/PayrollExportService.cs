using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using System.Text.Json;

namespace StatsTid.Integrations.Payroll.Services;

public sealed class PayrollExportService
{
    // ADR-033 slice 1b — fail-closed settlement-line delivery guard.
    // A settlement line is identified by its DATA (not a caller-supplied flag, which would be
    // bypassable): a placeholder sentinel wage_type prefix, or the §24 settlement time_type.
    // The placeholder lønart (SLS_TBD_*) is unverified against SLS and must NEVER leave the
    // system, even once non-sentinel settlement delivery is later enabled via config.
    private const string SettlementSentinelWageTypePrefix = "SLS_TBD_";
    private const string SettlementPayoutTimeType = "VACATION_SETTLEMENT_PAYOUT";

    // Fail-closed config gate for NON-sentinel settlement lines. Absent/not-exactly-"true" ⇒ disabled.
    private const string SettlementLineDeliveryEnabledKey = "Settlement:LineDeliveryEnabled";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PayrollExportService> _logger;
    private readonly string _mockPayrollUrl;

    public PayrollExportService(
        IHttpClientFactory httpClientFactory,
        DbConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<PayrollExportService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _logger = logger;
        _mockPayrollUrl = configuration["ServiceUrls:MockPayroll"] ?? "http://mock-payroll:8080";
    }

    public async Task<ExportResult> ExportAsync(
        IReadOnlyList<PayrollExportLine> lines, CancellationToken ct = default)
    {
        // ADR-033 slice 1b: fail-closed outbound delivery guard. This runs BEFORE the
        // outbox_messages INSERT (the outbound boundary), so a misconfigured settlement
        // delivery attempt fails loudly and NOTHING is written to the outbox. Non-settlement
        // lines (NORMAL_HOURS, OVERTIME, FLEX_PAYOUT, …) are completely unaffected.
        GuardSettlementLineDelivery(lines);

        var exportId = Guid.NewGuid().ToString();
        var payload = new { exportId, lines, exportedAt = DateTime.UtcNow };

        // Write to outbox first (guaranteed delivery pattern)
        var messageId = await WriteToOutboxAsync("payroll", payload, ct);

        // Send to mock payroll
        var client = _httpClientFactory.CreateClient();
        try
        {
            var response = await client.PostAsJsonAsync($"{_mockPayrollUrl}/api/payroll/receive", payload, ct);
            var success = response.IsSuccessStatusCode;

            await UpdateOutboxStatusAsync(messageId, success ? "delivered" : "failed", ct);

            _logger.LogInformation("Payroll export {ExportId}: {Status}", exportId, success ? "delivered" : "failed");

            return new ExportResult { ExportId = exportId, Success = success, MessageId = messageId };
        }
        catch (Exception ex)
        {
            await UpdateOutboxStatusAsync(messageId, "failed", ct, ex.Message);
            _logger.LogError(ex, "Payroll export {ExportId} failed", exportId);
            return new ExportResult { ExportId = exportId, Success = false, MessageId = messageId };
        }
    }

    // ADR-033 slice 1b — one of three independent locks (the others: the D13 go-live gate keeps
    // the emitter dormant pre-launch; no delivery path is wired to settlement lines this sprint).
    // This is the OUTBOUND guard: settlement export lines must not enter outbox_messages this
    // sprint, and a placeholder sentinel lønart must NEVER be deliverable even once delivery is
    // later enabled. Refuse LOUDLY (throw) rather than silently drop — a silent drop would hide a
    // real misconfigured delivery attempt.
    private void GuardSettlementLineDelivery(IReadOnlyList<PayrollExportLine> lines)
    {
        foreach (var line in lines)
        {
            // Discriminator strength (Step-7a FIX 4 — future-hardening). The `SLS_TBD_` sentinel-wage_type
            // prefix is the NON-bypassable guard for slice 1b: the emitter ALWAYS stamps the sentinel
            // lønart on a §24 line, so an `isSentinel` match below cannot be omitted by a caller. The
            // SourceTimeType == VACATION_SETTLEMENT_PAYOUT check is only a SECONDARY discriminator that IS
            // caller-omissible (a caller could construct a settlement line without setting SourceTimeType).
            // ⇒ When a FUTURE slice wires real delivery and replaces the sentinel with a real §24 SLS code,
            // the sentinel match disappears and this guard would rest on the omissible SourceTimeType
            // alone — at that point the discriminator MUST be strengthened to a non-omissible typed
            // line-kind (or settlement lines must move to a dedicated settlement delivery path). No
            // behavior change this sprint (the sentinel is still the load-bearing, unconditional refusal).
            var isSentinel = line.WageType is not null
                && line.WageType.StartsWith(SettlementSentinelWageTypePrefix, StringComparison.Ordinal);
            var isSettlementLine = isSentinel
                || string.Equals(line.SourceTimeType, SettlementPayoutTimeType, StringComparison.Ordinal);

            if (!isSettlementLine)
                continue;

            // The placeholder sentinel lønart is unverified against SLS — refuse unconditionally,
            // regardless of the delivery flag.
            if (isSentinel)
            {
                throw new InvalidOperationException(
                    $"Settlement export line delivery is disabled (ADR-033 slice 1b): placeholder sentinel " +
                    $"lønart wage_type='{line.WageType}' can never enter the outbox.");
            }

            // Non-sentinel settlement line: gated by config, fail-closed (absent/not-"true" ⇒ disabled).
            if (!SettlementLineDeliveryEnabled())
            {
                throw new InvalidOperationException(
                    $"Settlement export line delivery is disabled (ADR-033 slice 1b); line " +
                    $"wage_type='{line.WageType}' cannot enter the outbox. Set '{SettlementLineDeliveryEnabledKey}'=true to enable.");
            }
        }
    }

    private bool SettlementLineDeliveryEnabled() =>
        string.Equals(_configuration[SettlementLineDeliveryEnabledKey], "true", StringComparison.Ordinal);

    private async Task<Guid> WriteToOutboxAsync(string destination, object payload, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var messageId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_messages (message_id, destination, payload, status)
            VALUES (@messageId, @destination, @payload::jsonb, 'pending')
            """, conn);
        cmd.Parameters.AddWithValue("messageId", messageId);
        cmd.Parameters.AddWithValue("destination", destination);
        cmd.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload));

        await cmd.ExecuteNonQueryAsync(ct);
        return messageId;
    }

    private async Task UpdateOutboxStatusAsync(Guid messageId, string status, CancellationToken ct, string? error = null)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var sql = status == "delivered"
            ? "UPDATE outbox_messages SET status = @status, delivered_at = NOW(), attempt_count = attempt_count + 1 WHERE message_id = @messageId"
            : "UPDATE outbox_messages SET status = @status, last_attempt_at = NOW(), attempt_count = attempt_count + 1, error_message = @error WHERE message_id = @messageId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("messageId", messageId);
        cmd.Parameters.AddWithValue("status", status);
        if (status != "delivered")
            cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class ExportResult
{
    public required string ExportId { get; init; }
    public required bool Success { get; init; }
    public Guid MessageId { get; init; }
}
