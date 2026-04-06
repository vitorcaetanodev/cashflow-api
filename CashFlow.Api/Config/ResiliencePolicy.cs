using Polly;

public static class ResiliencePolicy
{
    public static IAsyncPolicy GetPolicy() =>
        Policy
        .Handle<Exception>()
        .CircuitBreakerAsync(3, TimeSpan.FromSeconds(10));
}