using Beloved.AssemblyEngine;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.ControlPlane.Services;

public class AssemblyWorker : BackgroundService
{
    private readonly IAssemblyQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<AssemblyHub> _hubContext;
    private readonly ILogger<AssemblyWorker> _logger;

    public AssemblyWorker(IAssemblyQueue queue, IServiceProvider serviceProvider, IHubContext<AssemblyHub> hubContext, ILogger<AssemblyWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Assembly Worker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);
            _logger.LogInformation("Processing Assembly Job: {JobId}", job.JobId);

            using var activity = BelovedDiagnostics.Source.StartActivity("AssembleJob");
            if (activity != null)
            {
                activity.SetTag("beloved.job_id", job.JobId);
                activity.SetTag("beloved.app_name", job.Blueprint?.AppName);
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var compiler = scope.ServiceProvider.GetRequiredService<AssemblyCompiler>();
                var outputStore = scope.ServiceProvider.GetRequiredService<IOutputStore>();
                var webhooks = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();
                var db = scope.ServiceProvider.GetRequiredService<BelovedDbContext>();

                await SendLogAsync(job.JobId, $"Dequeued assembly job {job.JobId}");

                // Resolve TenantId for webhook dispatch
                var jobRecord = await db.AssemblyJobs
                    .Include(j => j.Project)
                    .FirstOrDefaultAsync(j => j.QueueJobId == job.JobId, stoppingToken);

                var tenantId = jobRecord?.Project?.TenantId ?? Guid.Empty;
                var quotaService = scope.ServiceProvider.GetRequiredService<IQuotaService>();
                var jobStart = DateTime.UtcNow;

                // Fire job.queued webhook
                _ = webhooks.DispatchAsync(tenantId, "job.queued", new
                {
                    jobId = job.JobId,
                    appName = job.Blueprint?.AppName,
                    modules = job.Blueprint?.Modules
                }, stoppingToken);

                var blueprintJson = System.Text.Json.JsonSerializer.Serialize(job.Blueprint, AssemblyJsonContext.Default.Blueprint);

                var result = await compiler.AssembleAsync(blueprintJson, job.JobId, outputStore, onLog: async (logMessage) =>
                {
                    await SendLogAsync(job.JobId, logMessage);
                });

                if (jobRecord != null)
                {
                    var durationMs = (long)(DateTime.UtcNow - jobStart).TotalMilliseconds;
                    var moduleCount = job.Blueprint?.Modules?.Count ?? 0;

                    if (result.Success)
                    {
                        jobRecord.Status = "Completed";
                        jobRecord.CompletedAt = DateTime.UtcNow;
                        jobRecord.SbomJson = result.SbomJson;
                        await db.SaveChangesAsync(stoppingToken);
                        await SendLogAsync(job.JobId, "SUCCESS: Assembly pipeline complete.");

                        BelovedDiagnostics.AssemblyCompletedCounter.Add(1, 
                            new KeyValuePair<string, object?>("app.name", job.Blueprint?.AppName));

                        // ── Usage Metering ──────────────────────────────────────────────────
                        if (tenantId != Guid.Empty)
                        {
                            await quotaService.RecordUsageAsync(tenantId, job.JobId, durationMs, moduleCount, succeeded: true, stoppingToken);
                            BelovedDiagnostics.TenantAssembliesCounter.Add(1,
                                new KeyValuePair<string, object?>("tenant_id", tenantId.ToString()));
                        }

                        // Fire job.completed webhook
                        _ = webhooks.DispatchAsync(tenantId, "job.completed", new
                        {
                            jobId = job.JobId,
                            appName = job.Blueprint?.AppName,
                            sbomComponents = result.SbomJson.Length > 0 ? "included" : "none"
                        }, stoppingToken);
                    }
                    else
                    {
                        jobRecord.Status = "Failed";
                        jobRecord.CompletedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(stoppingToken);
                        await SendLogAsync(job.JobId, "ERROR: Assembly failed.", isError: true);

                        BelovedDiagnostics.AssemblyFailedCounter.Add(1, 
                            new KeyValuePair<string, object?>("app.name", job.Blueprint?.AppName));

                        // ── Usage Metering (failed jobs still count against quota) ───────────
                        if (tenantId != Guid.Empty)
                        {
                            await quotaService.RecordUsageAsync(tenantId, job.JobId, durationMs, moduleCount, succeeded: false, stoppingToken);
                            BelovedDiagnostics.TenantAssembliesCounter.Add(1,
                                new KeyValuePair<string, object?>("tenant_id", tenantId.ToString()));
                        }

                        // Fire job.failed webhook
                        _ = webhooks.DispatchAsync(tenantId, "job.failed", new
                        {
                            jobId = job.JobId,
                            reason = "Assembly returned failure"
                        }, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing job {JobId}", job.JobId);
                await SendLogAsync(job.JobId, $"FATAL ERROR: {ex.Message}", isError: true);

                BelovedDiagnostics.AssemblyFailedCounter.Add(1, 
                    new KeyValuePair<string, object?>("app.name", job.Blueprint?.AppName));

                using var errScope = _serviceProvider.CreateScope();
                var errDb = errScope.ServiceProvider.GetRequiredService<BelovedDbContext>();
                var webhooks = errScope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();

                var jobRecord = await errDb.AssemblyJobs
                    .Include(j => j.Project)
                    .FirstOrDefaultAsync(j => j.QueueJobId == job.JobId, stoppingToken);

                if (jobRecord != null)
                {
                    var tenantId = jobRecord.Project?.TenantId ?? Guid.Empty;
                    jobRecord.Status = "Failed";
                    jobRecord.CompletedAt = DateTime.UtcNow;
                    await errDb.SaveChangesAsync(stoppingToken);

                    _ = webhooks.DispatchAsync(tenantId, "job.failed", new
                    {
                        jobId = job.JobId,
                        reason = ex.Message
                    }, stoppingToken);
                }
            }
        }
    }

    private async Task SendLogAsync(string jobId, string message, bool isError = false)
    {
        await _hubContext.Clients.Group(jobId).SendAsync("ReceiveLog", message, isError ? "error" : "info");
    }
}

