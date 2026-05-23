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

        // CA5394 suppression rationale: jitter is for retry spread, not a security primitive.
        // Predictability of backoff intervals is not a threat — using crypto-secure RNG would
        // add cost with no security benefit. Standard practice for backoff jitter.
#pragma warning disable CA5394 // Do not use insecure randomness
        var jitter = Random.Shared.NextDouble() * delay.TotalMilliseconds * 0.1;
#pragma warning restore CA5394

        return delay + TimeSpan.FromMilliseconds(jitter);
    }
}
