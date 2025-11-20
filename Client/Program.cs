using Client.model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;

namespace Client;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: dotnet run <url> <port>");
            return -1;
        }
        
        var url = args[0];
        var id = int.Parse(args[1]);
        
        using IHost host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Register our typed client with a resilient HttpClient
                services
                    .AddHttpClient<ApiClient>(client =>
                    {
                        client.BaseAddress = new Uri(url);
                        // Let Polly / resilience libraries handle timeouts instead of HttpClient.Timeout
                    })
                    .AddResilienceHandler("advanced-pipeline", builder =>
                    {
                        // 1) Fallback – last resort if retries & circuit breaker still end in failure
                        builder.AddFallback(
                            ResiliencePipelines.CreateHttpFallbackOptions());

                        // 2) Retry – exponential backoff with jitter, for transient failures
                        builder.AddRetry(
                            ResiliencePipelines.CreateHttpRetryOptions());

                        // 3) Circuit breaker – stop hammering an unhealthy service
                        builder.AddCircuitBreaker(
                            ResiliencePipelines.CreateHttpCircuitBreakerOptions());
                    });
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var apiClient = host.Services.GetRequiredService<ApiClient>();

        try
        {
            logger.LogInformation("Calling remote API via ApiClient...");

            // Try something that often fails (500) to see retry + fallback in action
            // string content = await apiClient.GetStatusAsync(500);
            var w = await apiClient.GetWidgetAsync(id);

            // logger.LogInformation("Final response body:\n{Body}", content);
            logger.LogInformation("Widget details. id = {w.Id}, name = {w.Name}", w.Id, w.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Call failed even after resilience pipeline.");
        }

        Console.WriteLine("Done. Press any key to exit.");
        Console.ReadKey();   
        
        return 0;
    }
}