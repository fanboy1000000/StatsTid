using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using System.Text.Json;

namespace StatsTid.Integrations.Payroll.Services;

public sealed class PayrollExportService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DbConnectionFactory _connectionFactory;
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
        _logger = logger;
        _mockPayrollUrl = configuration["ServiceUrls:MockPayroll"] ?? "http://mock-payroll:8080";
    }

    public async Task<ExportResult> ExportAsync(
        IReadOnlyList<PayrollExportLine> lines, CancellationToken ct = default)
    {
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
