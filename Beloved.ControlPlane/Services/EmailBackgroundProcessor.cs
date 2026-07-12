using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Beloved.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Beloved.ControlPlane.Services;

/// <summary>
/// Resilient, cloud-native background worker that polls the transactional outbox table
/// for Pending/Failed email jobs, processes them, handles retry backoff limits, and updates
/// status in the DB.
/// </summary>
public sealed class EmailBackgroundProcessor : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmailBackgroundProcessor> _logger;

    public EmailBackgroundProcessor(IServiceProvider serviceProvider, ILogger<EmailBackgroundProcessor> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Email outbox background processor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEmailsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queued outbox emails.");
            }

            // Poll outbox queue every 10 seconds (in production, config-driven or SignalR-triggered)
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessPendingEmailsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BelovedDbContext>();
        var mailClient = scope.ServiceProvider.GetRequiredService<IMailClient>();

        var pendingJobs = await db.EmailQueueJobs
            .Where(j => j.Status == "Pending" || (j.Status == "Failed" && j.RetryCount < 3))
            .OrderBy(j => j.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        if (pendingJobs.Count == 0) return;

        foreach (var job in pendingJobs)
        {
            try
            {
                _logger.LogInformation("Processing email job {Id} to {Recipient}", job.Id, job.RecipientEmail);

                // Leverage pluggable mail client with resilient exponential retry logic
                await mailClient.SendEmailAsync(job.RecipientEmail, job.Subject, job.Body);

                job.Status = "Sent";
                job.ProcessedAt = DateTime.UtcNow;
                job.ErrorMessage = null;
            }
            catch (Exception ex)
            {
                job.RetryCount++;
                job.ErrorMessage = ex.Message;
                job.Status = job.RetryCount >= 3 ? "DeadLetter" : "Failed";
                _logger.LogError(ex, "Failed to deliver email job {Id}. Attempt: {Count}", job.Id, job.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
