using StatsTid.Infrastructure.Resilience;

namespace StatsTid.Tests.Unit;

public class CircuitBreakerTests
{
    [Fact]
    public void InitialState_IsClosed()
    {
        var cb = new CircuitBreaker(failureThreshold: 3);
        Assert.Equal(CircuitState.Closed, cb.State);
        Assert.True(cb.IsAllowed);
    }

    [Fact]
    public void AfterThresholdFailures_TransitionsToOpen()
    {
        var cb = new CircuitBreaker(failureThreshold: 3, resetTimeout: TimeSpan.FromMinutes(5));

        cb.RecordFailure();
        cb.RecordFailure();
        Assert.Equal(CircuitState.Closed, cb.State);

        cb.RecordFailure();
        Assert.Equal(CircuitState.Open, cb.State);
        Assert.False(cb.IsAllowed);
    }

    [Fact]
    public void SuccessAfterFailures_ResetsToClosed()
    {
        var cb = new CircuitBreaker(failureThreshold: 3);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordSuccess();

        Assert.Equal(CircuitState.Closed, cb.State);
        Assert.True(cb.IsAllowed);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var cb = new CircuitBreaker(failureThreshold: 2);

        cb.RecordFailure();
        cb.RecordFailure();
        Assert.Equal(CircuitState.Open, cb.State);

        cb.Reset();
        Assert.Equal(CircuitState.Closed, cb.State);
        Assert.True(cb.IsAllowed);
    }
}
