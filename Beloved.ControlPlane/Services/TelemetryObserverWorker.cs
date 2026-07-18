using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MassTransit;

namespace Beloved.ControlPlane.Services;

public class TelemetryObserverWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TelemetryObserverWorker> _logger;

    public TelemetryObserverWorker(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory, ILogger<TelemetryObserverWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Self-optimizing telemetry observer worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Query local Prometheus exporter (/metrics)
                // If average compilation latency spans exceed 1000ms, trigger build optimization
                double latency = await GetAverageAssemblyLatencyAsync(stoppingToken);
                if (latency > 1000.0)
                {
                    _logger.LogWarning("Telemetry warning: Assembly latency average ({Latency}ms) exceeded threshold. Queueing optimization job...", latency);
                    
                    using var scope = _serviceProvider.CreateScope();
                    var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                    
                    // Trigger optimization event
                    await publishEndpoint.Publish(new OptimizeAssemblyMessage("AllModules", "Latency threshold exceeded"));
                    _logger.LogInformation("Dynamic optimization loop successfully queued.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in telemetry observer.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    protected virtual async Task<double> GetAverageAssemblyLatencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetStringAsync("http://localhost:3000/metrics", cancellationToken);
            
            double sum = 0.0;
            double count = 0.0;
            
            using var reader = new StringReader(response);
            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("beloved_assembly_duration_seconds_sum"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) double.TryParse(parts[1], out sum);
                }
                else if (line.StartsWith("beloved_assembly_duration_seconds_count"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1) double.TryParse(parts[1], out count);
                }
            }
            
            if (count > 0)
            {
                return (sum / count) * 1000.0; // Return in milliseconds
            }
        }
        catch
        {
            // Fall back to safe baseline if local collector is offline or not scraped yet
        }
        return 0.0; 
    }
}
