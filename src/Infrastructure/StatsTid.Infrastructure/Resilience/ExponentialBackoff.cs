namespace StatsTid.Infrastructure.Resilience;

/// <summary>
/// Calculates exponential backoff delay with optional jitter.
/// </summary>
public static class ExponentialBackoff
{
    public static TimeSpan Calculate(int attemptCount, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        var @base = baseDelay ?? TimeSpan.FromSeconds(1);
        var max = maxDelay ?? TimeSpan.FromSeconds(60);

        var delayMs = @base.TotalMilliseconds * Math.Pow(2, attemptCount);
        var clampedMs = Math.Min(delayMs, max.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(clampedMs);
    }

    public static TimeSpan CalculateWithJitter(int attemptCount, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        var delay = Calculate(attemptCount, baseDelay, maxDelay);
        var jitter = Random.Shared.NextDouble() * delay.TotalMilliseconds * 0.1;
        return delay + TimeSpan.FromMilliseconds(jitter);
    }
}
