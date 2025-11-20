using System.Net;
using System.Text;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Fallback;

namespace Client;

public static class ResiliencePipelines
{
    /// <summary>
    /// Fallback strategy: if everything else fails, return a synthetic 200 OK with body.
    /// </summary>
    public static FallbackStrategyOptions<HttpResponseMessage> CreateHttpFallbackOptions()
    {
        // When should fallback trigger?
        // - Any HttpRequestException
        // - Any non-success HTTP status code
        var shouldHandle = new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(r =>
                !r.IsSuccessStatusCode); // 4xx/5xx etc.  [oai_citation:1‡pollydocs.org](https://www.pollydocs.org/strategies/fallback.html?utm_source=chatgpt.com)

        return new FallbackStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = shouldHandle,
            FallbackAction = static args =>
            {
                var fallbackResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"message\":\"served from Polly fallback\"}",
                        Encoding.UTF8,
                        "application/json")
                };

                // Mark this so the typed client can log that fallback was used
                fallbackResponse.Headers.Add("X-Fallback", "true");

                return Outcome.FromResultAsValueTask(fallbackResponse);
            }
        };
    }

    /// <summary>
    /// Retry strategy with exponential backoff and jitter for transient HTTP failures.
    /// </summary>
    public static HttpRetryStrategyOptions CreateHttpRetryOptions()
    {
        return new HttpRetryStrategyOptions
        {
            // Defaults already handle transient HTTP errors (408, 429, 5xx, HttpRequestException, TimeoutRejectedException)  [oai_citation:2‡Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.http.resilience.httpretrystrategyoptions.-ctor?view=net-9.0-pp&utm_source=chatgpt.com)
            MaxRetryAttempts = 3,
            // Delay = TimeSpan.FromSeconds(1),
            // Custom delay per attempt
            DelayGenerator = static args =>
            {
                // AttemptNumber:
                // 0 = original call
                // 1..MaxRetryAttempts = retries

                if (args.AttemptNumber == 0)
                {
                    // Original attempt: no delay
                    return new ValueTask<TimeSpan?>(TimeSpan.Zero);
                }

                // Exponential backoff: 2^attempt seconds
                var delaySeconds = Math.Pow(2, args.AttemptNumber); // 2, 4, 8, 16...
                var delay = TimeSpan.FromSeconds(delaySeconds);

                // Optional: cap the delay so it never exceeds 30 seconds
                if (delay > TimeSpan.FromSeconds(30))
                {
                    delay = TimeSpan.FromSeconds(30);
                }

                return new ValueTask<TimeSpan?>(delay);
            },
            BackoffType =
                DelayBackoffType
                    .Exponential, // 1s, 2s, 4s...  [oai_citation:3‡pollydocs.org](https://www.pollydocs.org/api/Polly.DelayBackoffType.html?utm_source=chatgpt.com)
            UseJitter = true
        };
    }

    /// <summary>
    /// Circuit breaker strategy: open the circuit when too many failures in a window.
    /// </summary>
    public static HttpCircuitBreakerStrategyOptions CreateHttpCircuitBreakerOptions()
    {
        return new HttpCircuitBreakerStrategyOptions
        {
            // Break when >= 50% of calls fail in a 30s window, with at least 4 calls  [oai_citation:4‡Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.http.resilience.httpcircuitbreakerstrategyoptions?view=net-9.0-pp&utm_source=chatgpt.com)
            FailureRatio = 0.5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 4,

            // Stay open for 15s before trying a "test" request
            BreakDuration = TimeSpan.FromSeconds(15)
        };
    }
}