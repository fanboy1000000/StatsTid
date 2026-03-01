namespace StatsTid.Infrastructure.Resilience;

/// <summary>
/// Simple circuit breaker: Closed → Open (after threshold failures) → HalfOpen (after timeout) → Closed/Open.
/// Thread-safe via lock.
/// </summary>
public sealed class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _resetTimeout;
    private readonly object _lock = new();

    private CircuitState _state = CircuitState.Closed;
    private int _failureCount;
    private DateTime _lastFailureTime;

    public CircuitBreaker(int failureThreshold = 5, TimeSpan? resetTimeout = null)
    {
        _failureThreshold = failureThreshold;
        _resetTimeout = resetTimeout ?? TimeSpan.FromSeconds(30);
    }

    public CircuitState State
    {
        get
        {
            lock (_lock)
            {
                if (_state == CircuitState.Open &&
                    DateTime.UtcNow - _lastFailureTime >= _resetTimeout)
                {
                    _state = CircuitState.HalfOpen;
                }
                return _state;
            }
        }
    }

    public bool IsAllowed => State != CircuitState.Open;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, Func<T>? fallback = null)
    {
        if (!IsAllowed)
        {
            if (fallback is not null) return fallback();
            throw new CircuitBreakerOpenException("Circuit breaker is open");
        }

        try
        {
            var result = await action();
            RecordSuccess();
            return result;
        }
        catch
        {
            RecordFailure();
            throw;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_failureCount >= _failureThreshold)
            {
                _state = CircuitState.Open;
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
        }
    }
}

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}
