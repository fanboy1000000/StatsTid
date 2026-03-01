using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Resilience;
using System.Text.Json;

namespace StatsTid.Integrations.External.Services;

public sealed class EventConsumerService : BackgroundService
{
    private const int MaxRetries = 10;
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ExternalApiClient _apiClient;
    private readonly DeliveryTracker _tracker;
    private readonly ILogger<EventConsumerService> _logger;
    private readonly CircuitBreaker _circuitBreaker;

    public EventConsumerService(
        DbConnectionFactory connectionFactory,
        ExternalApiClient apiClient,
        DeliveryTracker tracker,
        ILogger<EventConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _apiClient = apiClient;
        _tracker = tracker;
        _logger = logger;
        _circuitBreaker = new CircuitBreaker(failureThreshold: 5, resetTimeout: TimeSpan.FromSeconds(30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Event consumer started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_circuitBreaker.IsAllowed)
                {
                    _logger.LogWarning("Circuit breaker is open, skipping polling cycle");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                var messages = await GetPendingMessagesWithLockAsync(stoppingToken);

                foreach (var (messageId, payload, attemptCount) in messages)
                {
                    _logger.LogInformation("Processing outbox message {MessageId} (attempt {Attempt})", messageId, attemptCount + 1);

                    if (attemptCount >= MaxRetries)
                    {
                        _logger.LogWarning("Message {MessageId} exceeded max retries, marking as dead_letter", messageId);
                        await _tracker.MarkDeadLetterAsync(messageId, "Max retries exceeded", stoppingToken);
                        continue;
                    }

                    try
                    {
                        var result = await _apiClient.SendAsync(
                            JsonSerializer.Deserialize<object>(payload)!, correlationId: null, ct: stoppingToken);

                        if (result.Success)
                        {
                            await _tracker.MarkDeliveredAsync(messageId, stoppingToken);
                            _circuitBreaker.RecordSuccess();
                        }
                        else
                        {
                            await _tracker.MarkFailedAsync(messageId, result.ErrorMessage ?? "Unknown error", stoppingToken);
                            _circuitBreaker.RecordFailure();
                        }
                    }
                    catch (Exception ex)
                    {
                        await _tracker.MarkFailedAsync(messageId, ex.Message, stoppingToken);
                        _circuitBreaker.RecordFailure();
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in event consumer loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task<List<(Guid MessageId, string Payload, int AttemptCount)>> GetPendingMessagesWithLockAsync(CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT message_id, payload::text, attempt_count FROM outbox_messages
            WHERE status IN ('pending', 'failed') AND destination = 'external'
            ORDER BY created_at ASC
            LIMIT 10
            FOR UPDATE SKIP LOCKED
            """, conn);

        var results = new List<(Guid, string, int)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return results;
    }
}
