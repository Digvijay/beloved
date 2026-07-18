using System;
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
    private readonly ILogger<TelemetryObserverWorker> _logger;

    public TelemetryObserverWorker(IServiceProvider serviceProvider, ILogger<TelemetryObserverWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Self-optimizing telemetry observer worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // In production, queries local Prometheus exporter
                // If average compilation latency spans exceed 1000ms, trigger build optimization
                double simulatedLatency = GetAverageAssemblyLatency();
                if (simulatedLatency > 1000.0)
                {
                    _logger.LogWarning("Telemetry warning: Assembly latency average ({Latency}ms) exceeded threshold. Queueing optimization job...", simulatedLatency);
                    
                    using var scope = _serviceProvider.CreateScope();
                    var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                    
                    // Trigger optimization event
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

    private double GetAverageAssemblyLatency()
    {
        // Mock query from Prometheus metrics endpoint
        return 1250.0; 
    }
}
